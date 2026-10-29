// Unbound's flat C API over PhysX 4.1 (the release the Unity 6000.0 Editor runs) for the managed 3D
// physics (UnityEngine.Physics).
//
// The API is the one native/unbound_box3d.c had (the managed binding is shared): worlds are uint32_t
// ids (a scene each), bodies and shapes uint64_t (their PxRigidActor* and PxShape*). Vectors and
// quaternions are {x, y, z} and {x, y, z, w}: Unity's Vector3 and Quaternion, PhysX's PxVec3 and
// PxQuat. The scene is set up as Unity sets up its own (PCM contacts, patch friction, PGS, sweep and
// prune, CCD available, contact offset 0.01, 6 position and 1 velocity solver iterations).
//
// PhysX's symbols stay hidden (-fvisibility=hidden); only the ua3_ functions are exported.

#include "PxPhysicsAPI.h"
#include "vehicle/PxVehicleSDK.h"
#include "vehicle/PxVehicleNoDrive.h"
#include "vehicle/PxVehicleUpdate.h"
#include "vehicle/PxVehicleUtilSetup.h"
#include "vehicle/PxVehicleTireFriction.h"
#include "PxVehicleSuspWheelTire4.h"
#include "characterkinematic/PxCapsuleController.h"
#include "characterkinematic/PxControllerManager.h"

#include <cstdint>
#include <cstdlib>
#if !defined( _WIN32 )
	#include <csignal>
	#include <execinfo.h>
	#include <unistd.h>
#endif
#include <cstdio>
#include <cstring>
#include <map>
#include <set>
#include <tuple>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>
#include <algorithm>

using namespace physx;

#if defined( _WIN32 )
	#define UA3_API extern "C" __declspec( dllexport )
#else
	#define UA3_API extern "C" __attribute__( ( visibility( "default" ) ) )
#endif

// The version of this API; the managed side checks it.
#define UA3_VERSION 4

// Vectors and quaternions cross the API as plain structs: PxVec3 and PxQuat have user-defined
// copy constructors, which the C ABI passes by reference instead of in registers.
struct ua3_vec3
{
	float x, y, z;
	ua3_vec3() = default;
	ua3_vec3( const PxVec3& v ) : x( v.x ), y( v.y ), z( v.z ) {}
	operator PxVec3() const
	{
		return PxVec3( x, y, z );
	}
};

struct ua3_quat
{
	float x, y, z, w;
	operator PxQuat() const
	{
		return PxQuat( x, y, z, w );
	}
};

namespace
{
// ------------------------------------------------------------------------------------ the SDK

class ErrorCallback : public PxErrorCallback
{
public:
	void reportError( PxErrorCode::Enum code, const char* message, const char* file, int line ) override
	{
		if ( code == PxErrorCode::eDEBUG_INFO || code == PxErrorCode::ePERF_WARNING )
		{
			return;
		}
		std::fprintf( stderr, "PhysX: %s (%s:%d)\n", message, file, line );
	}
};

PxDefaultAllocator s_allocator;
ErrorCallback s_errors;
PxFoundation* s_foundation;
PxPhysics* s_physics;
PxCooking* s_cooking;
PxDefaultCpuDispatcher* s_dispatcher;

// Unity's defaults (Physics settings).
float s_contactOffset = 0.01f;
float s_sleepThreshold = 0.005f;
float s_maxDepenetration = 10.0f;
float s_maxAngularVelocity = 50.0f;
int s_positionIterations = 6;
int s_velocityIterations = 1;

#if !defined( _WIN32 )
// UA3_DEBUG_CRASH=1: a crash in native code prints its stack (addresses for addr2line) to stderr.
void crash_handler( int signal )
{
	void* frames[64];
	int count = backtrace( frames, 64 );
	char line[64];
	int n = std::snprintf( line, sizeof( line ), "ua3: signal %d\n", signal );
	(void)!write( 2, line, static_cast<size_t>( n ) );
	backtrace_symbols_fd( frames, count, 2 );
	_exit( 128 + signal );
}
#endif

bool sdk()
{
	if ( s_physics != nullptr )
	{
		return true;
	}
#if !defined( _WIN32 )
	if ( std::getenv( "UA3_DEBUG_CRASH" ) != nullptr )
	{
		std::signal( SIGSEGV, crash_handler );
		std::signal( SIGABRT, crash_handler );
	}
#endif
	s_foundation = PxCreateFoundation( PX_PHYSICS_VERSION, s_allocator, s_errors );
	if ( s_foundation == nullptr )
	{
		return false;
	}
	PxTolerancesScale scale;
	s_physics = PxCreatePhysics( PX_PHYSICS_VERSION, *s_foundation, scale, false, nullptr );
	if ( s_physics == nullptr )
	{
		return false;
	}
	PxInitExtensions( *s_physics, nullptr );
	// Vehicles as Unity sets them up: y up, z forward, velocity changes on the chassis.
	PxInitVehicleSDK( *s_physics );
	PxVehicleSetBasisVectors( PxVec3( 0.0f, 1.0f, 0.0f ), PxVec3( 0.0f, 0.0f, 1.0f ) );
	PxVehicleSetUpdateMode( PxVehicleUpdateMode::eVELOCITY_CHANGE );
	PxCookingParams params( scale );
	// Unity's cooking: welding and cleaning on, the fast midphase.
	params.meshPreprocessParams = PxMeshPreprocessingFlags( PxMeshPreprocessingFlag::eWELD_VERTICES );
	params.meshWeldTolerance = 0.0f;
	params.midphaseDesc = PxMeshMidPhase::eBVH34;
	s_cooking = PxCreateCooking( PX_PHYSICS_VERSION, *s_foundation, params );
	// All tasks on the stepping thread: deterministic, as the managed side expects.
	s_dispatcher = PxDefaultCpuDispatcherCreate( 0 );
	return s_cooking != nullptr && s_dispatcher != nullptr;
}

// ------------------------------------------------------------------------------------- shapes

// What a shape carries besides its PhysX state.
struct ShapeInfo
{
	float density;
	bool contactEvents;
	bool sensorEvents;
	int ignores; // how many ignored pairs it is in
};

// Simulation filter data: word0 the category (the layer's bit), word1 the mask, word2 flags.
enum : PxU32
{
	FlagContactEvents = 1,
	FlagSensorEvents = 2,
	FlagIgnores = 4,
};

std::unordered_set<PxRigidActor*> s_bodies;
std::unordered_set<PxShape*> s_shapes;
std::unordered_set<PxJoint*> s_joints;

ShapeInfo* info( const PxShape* shape )
{
	return static_cast<ShapeInfo*>( shape->userData );
}

uint64_t pack( const void* p )
{
	return static_cast<uint64_t>( reinterpret_cast<uintptr_t>( p ) );
}

PxRigidActor* body_of( uint64_t id )
{
	PxRigidActor* actor = reinterpret_cast<PxRigidActor*>( static_cast<uintptr_t>( id ) );
	return s_bodies.count( actor ) ? actor : nullptr;
}

PxRigidDynamic* dynamic_of( uint64_t id )
{
	PxRigidActor* actor = body_of( id );
	return actor != nullptr ? actor->is<PxRigidDynamic>() : nullptr;
}

PxJoint* joint_of( uint64_t id )
{
	PxJoint* joint = reinterpret_cast<PxJoint*>( static_cast<uintptr_t>( id ) );
	return s_joints.count( joint ) ? joint : nullptr;
}

PxD6Joint* d6_of( uint64_t id )
{
	PxJoint* joint = joint_of( id );
	return joint != nullptr ? joint->is<PxD6Joint>() : nullptr;
}

// A joint limit's contact distance from the managed side: > 0 as it is, 0 the hinge's 0.01 rad,
// < 0 PhysX's default (the lesser of 0.1 rad and 0.49 of the limit's span) (measured).
PxReal contact_distance( float value )
{
	if ( value > 0.0f )
	{
		return value;
	}
	return value == 0.0f ? 0.01f : -1.0f;
}

PxShape* shape_of( uint64_t id )
{
	PxShape* shape = reinterpret_cast<PxShape*>( static_cast<uintptr_t>( id ) );
	return s_shapes.count( shape ) ? shape : nullptr;
}

// --------------------------------------------------------------------------------- materials

// A material per (static friction, dynamic friction, bounce, combine modes); Unity's combine modes
// are PhysX's (0 average, 1 minimum, 2 multiply, 3 maximum; of two the larger wins).
std::map<std::tuple<float, float, float, int>, PxMaterial*> s_materials;

PxMaterial* material( float staticFriction, float dynamicFriction, float restitution, uint64_t modes )
{
	auto key = std::make_tuple( staticFriction, dynamicFriction, restitution, static_cast<int>( modes & 15 ) );
	auto found = s_materials.find( key );
	if ( found != s_materials.end() )
	{
		return found->second;
	}
	PxMaterial* m = s_physics->createMaterial( staticFriction, dynamicFriction, restitution );
	m->setFrictionCombineMode( static_cast<PxCombineMode::Enum>( modes & 3 ) );
	m->setRestitutionCombineMode( static_cast<PxCombineMode::Enum>( ( modes >> 2 ) & 3 ) );
	s_materials[key] = m;
	return m;
}

// ------------------------------------------------------------------------------------- worlds

struct ua3_pair
{
	uint64_t a, b;
};

struct ua3_contact
{
	uint64_t shapeA, shapeB;
	PxVec3 point;
	PxVec3 normal; // from A to B
	float separation;
	float normalImpulse;
	PxVec3 impulse; // as PhysX reports it (applied to the first shape of PhysX's pair)
};

struct World;

class Events : public PxSimulationEventCallback
{
public:
	World* world = nullptr;
	void onConstraintBreak( PxConstraintInfo* constraints, PxU32 count ) override;
	void onWake( PxActor**, PxU32 ) override {}
	void onSleep( PxActor**, PxU32 ) override {}
	void onAdvance( const PxRigidBody* const*, const PxTransform*, const PxU32 ) override {}
	void onContact( const PxContactPairHeader& header, const PxContactPair* pairs, PxU32 count ) override;
	void onTrigger( PxTriggerPair* pairs, PxU32 count ) override;
};

class Filter : public PxSimulationFilterCallback
{
public:
	World* world = nullptr;
	PxFilterFlags pairFound( PxU32, PxFilterObjectAttributes, PxFilterData, const PxActor*, const PxShape* shape0,
							 PxFilterObjectAttributes, PxFilterData, const PxActor*, const PxShape* shape1,
							 PxPairFlags& ) override;
	void pairLost( PxU32, PxFilterObjectAttributes, PxFilterData, PxFilterObjectAttributes, PxFilterData, bool ) override {}
	bool statusChange( PxU32&, PxPairFlags&, PxFilterFlags& ) override
	{
		return false;
	}
};

struct World
{
	PxScene* scene = nullptr;
	Events events;
	Filter filter;
	std::vector<ua3_pair> lists[4]; // contact begin, contact end, sensor begin, sensor end
	std::set<std::pair<PxShape*, PxShape*>> ignores;
	// The contact points of touching pairs this step.
	std::map<std::pair<PxShape*, PxShape*>, std::vector<ua3_contact>> contacts;
	std::vector<std::pair<uint64_t, float>> broken; // joints broken in the last step, with the force
	std::vector<struct Vehicle*> vehicles;
	PxBatchQuery* wheelQuery = nullptr;
	std::vector<PxRaycastQueryResult> wheelResults;
	std::vector<PxRaycastHit> wheelHits;
	PxControllerManager* controllers = nullptr; // created with the first character controller
	bool sleeping = true;
};

std::vector<World*> s_worlds;

World* world_of( uint32_t id )
{
	return id >= 1 && id <= s_worlds.size() ? s_worlds[id - 1] : nullptr;
}

World* world_of_actor( const PxActor* actor )
{
	PxScene* scene = actor != nullptr ? actor->getScene() : nullptr;
	return scene != nullptr ? static_cast<World*>( scene->userData ) : nullptr;
}

std::pair<PxShape*, PxShape*> ordered( PxShape* a, PxShape* b )
{
	return a < b ? std::make_pair( a, b ) : std::make_pair( b, a );
}

void Events::onContact( const PxContactPairHeader& header, const PxContactPair* pairs, PxU32 count )
{
	(void)header;
	std::vector<PxContactPairPoint> points;
	for ( PxU32 i = 0; i < count; ++i )
	{
		const PxContactPair& pair = pairs[i];
		if ( pair.flags & ( PxContactPairFlag::eREMOVED_SHAPE_0 | PxContactPairFlag::eREMOVED_SHAPE_1 ) )
		{
			continue;
		}
		PxShape* a = pair.shapes[0];
		PxShape* b = pair.shapes[1];
		bool reported = ( ( info( a ) && info( a )->contactEvents ) || ( info( b ) && info( b )->contactEvents ) );
		if ( reported && ( pair.events & PxPairFlag::eNOTIFY_TOUCH_FOUND ) )
		{
			world->lists[0].push_back( { pack( a ), pack( b ) } );
		}
		if ( reported && ( pair.events & PxPairFlag::eNOTIFY_TOUCH_LOST ) )
		{
			world->lists[1].push_back( { pack( a ), pack( b ) } );
		}
		if ( pair.events & PxPairFlag::eNOTIFY_TOUCH_LOST )
		{
			world->contacts.erase( ordered( a, b ) );
			continue;
		}
		if ( pair.contactCount == 0 )
		{
			continue;
		}
		points.resize( pair.contactCount );
		PxU32 n = pair.extractContacts( points.data(), pair.contactCount );
		std::vector<ua3_contact>& list = world->contacts[ordered( a, b )];
		list.clear();
		for ( PxU32 p = 0; p < n; ++p )
		{
			// PhysX's normal points from the second shape to the first.
			ua3_contact c;
			c.shapeA = pack( a );
			c.shapeB = pack( b );
			c.point = points[p].position;
			c.normal = -points[p].normal;
			c.separation = points[p].separation;
			c.normalImpulse = PxAbs( points[p].impulse.dot( points[p].normal ) );
			c.impulse = points[p].impulse;
			list.push_back( c );
		}
	}
}

void Events::onConstraintBreak( PxConstraintInfo* constraints, PxU32 count )
{
	for ( PxU32 i = 0; i < count; ++i )
	{
		if ( constraints[i].type != PxConstraintExtIDs::eJOINT )
		{
			continue;
		}
		PxJoint* joint = static_cast<PxJoint*>( constraints[i].externalReference );
		// The force is read once the step's results are in (ua3_world_broken_joints).
		world->broken.push_back( { pack( joint ), 0.0f } );
	}
}

void Events::onTrigger( PxTriggerPair* pairs, PxU32 count )
{
	for ( PxU32 i = 0; i < count; ++i )
	{
		const PxTriggerPair& pair = pairs[i];
		if ( pair.flags & ( PxTriggerPairFlag::eREMOVED_SHAPE_TRIGGER | PxTriggerPairFlag::eREMOVED_SHAPE_OTHER ) )
		{
			continue;
		}
		if ( pair.status & PxPairFlag::eNOTIFY_TOUCH_FOUND )
		{
			world->lists[2].push_back( { pack( pair.triggerShape ), pack( pair.otherShape ) } );
		}
		if ( pair.status & PxPairFlag::eNOTIFY_TOUCH_LOST )
		{
			world->lists[3].push_back( { pack( pair.triggerShape ), pack( pair.otherShape ) } );
		}
	}
}

PxFilterFlags Filter::pairFound( PxU32, PxFilterObjectAttributes, PxFilterData, const PxActor*, const PxShape* shape0,
								 PxFilterObjectAttributes, PxFilterData, const PxActor*, const PxShape* shape1, PxPairFlags& )
{
	if ( world->ignores.count( ordered( const_cast<PxShape*>( shape0 ), const_cast<PxShape*>( shape1 ) ) ) )
	{
		return PxFilterFlag::eSUPPRESS;
	}
	return PxFilterFlag::eDEFAULT;
}

// Layers collide when each one's category is in the other's mask. Triggers report with any body
// but another trigger; contacts need a dynamic body (kinematic and static pairs only touch
// triggers), and report their points for collision messages.
PxFilterFlags shader( PxFilterObjectAttributes attributes0, PxFilterData data0, PxFilterObjectAttributes attributes1,
					  PxFilterData data1, PxPairFlags& pairFlags, const void*, PxU32 )
{
	if ( ( data0.word0 & data1.word1 ) == 0 || ( data1.word0 & data0.word1 ) == 0 )
	{
		return PxFilterFlag::eSUPPRESS;
	}
	bool trigger0 = PxFilterObjectIsTrigger( attributes0 ), trigger1 = PxFilterObjectIsTrigger( attributes1 );
	if ( trigger0 || trigger1 )
	{
		if ( trigger0 && trigger1 )
		{
			return PxFilterFlag::eSUPPRESS;
		}
		pairFlags = PxPairFlag::eTRIGGER_DEFAULT;
		return ( data0.word2 | data1.word2 ) & FlagIgnores ? PxFilterFlag::eCALLBACK : PxFilterFlag::eDEFAULT;
	}
	bool moving0 = PxGetFilterObjectType( attributes0 ) == PxFilterObjectType::eRIGID_DYNAMIC && !PxFilterObjectIsKinematic( attributes0 );
	bool moving1 = PxGetFilterObjectType( attributes1 ) == PxFilterObjectType::eRIGID_DYNAMIC && !PxFilterObjectIsKinematic( attributes1 );
	if ( !moving0 && !moving1 )
	{
		return PxFilterFlag::eSUPPRESS;
	}
	pairFlags = PxPairFlag::eCONTACT_DEFAULT | PxPairFlag::eNOTIFY_TOUCH_FOUND | PxPairFlag::eNOTIFY_TOUCH_LOST |
				PxPairFlag::eNOTIFY_TOUCH_PERSISTS | PxPairFlag::eNOTIFY_CONTACT_POINTS | PxPairFlag::eDETECT_CCD_CONTACT;
	return ( data0.word2 | data1.word2 ) & FlagIgnores ? PxFilterFlag::eCALLBACK : PxFilterFlag::eDEFAULT;
}

void forget_shape( World* world, PxShape* shape )
{
	if ( world != nullptr )
	{
		for ( auto it = world->ignores.begin(); it != world->ignores.end(); )
		{
			if ( it->first == shape || it->second == shape )
			{
				ShapeInfo* other = info( it->first == shape ? it->second : it->first );
				if ( other != nullptr && other->ignores > 0 )
				{
					--other->ignores;
				}
				it = world->ignores.erase( it );
			}
			else
			{
				++it;
			}
		}
		for ( auto it = world->contacts.begin(); it != world->contacts.end(); )
		{
			it = ( it->first.first == shape || it->first.second == shape ) ? world->contacts.erase( it ) : std::next( it );
		}
	}
	delete info( shape );
	shape->userData = nullptr;
	s_shapes.erase( shape );
}

void refilter( PxShape* shape )
{
	PxRigidActor* actor = shape->getActor();
	if ( actor != nullptr && actor->getScene() != nullptr )
	{
		actor->getScene()->resetFiltering( *actor, &shape, 1 );
	}
}

void set_filter_flags( PxShape* shape )
{
	ShapeInfo* i = info( shape );
	PxFilterData data = shape->getSimulationFilterData();
	data.word2 = ( i->contactEvents ? FlagContactEvents : 0u ) | ( i->sensorEvents ? FlagSensorEvents : 0u ) | ( i->ignores > 0 ? FlagIgnores : 0u );
	shape->setSimulationFilterData( data );
}

// ------------------------------------------------------------------------------------ vehicles

// Unity's tire: per axis a friction curve (a cubic Hermite from (0, 0) with the chord's slope up
// to the extremum with a zero tangent, another to the asymptote with zero tangents, then flat)
// times its stiffness and the filtered tire load, against the slip; the pair kept inside the
// ellipse of the two curves' extremum forces (measured: tests/projects/vehicles).
struct TireCurves
{
	float forward[5]; // extremum slip, extremum value, asymptote slip, asymptote value, stiffness
	float sideways[5];
};

PxF32 curve( const float* c, PxF32 slip )
{
	PxF32 a = PxAbs( slip );
	PxF32 exS = c[0], exV = c[1], asS = c[2], asV = c[3];
	if ( a <= exS )
	{
		if ( exS <= 0.0f )
		{
			return exV;
		}
		PxF32 t = a / exS, t2 = t * t, t3 = t2 * t;
		return ( t3 - 2.0f * t2 + t ) * exV + ( 3.0f * t2 - 2.0f * t3 ) * exV;
	}
	if ( a <= asS )
	{
		PxF32 t = ( a - exS ) / ( asS - exS ), t2 = t * t, t3 = t2 * t;
		return exV + ( asV - exV ) * ( 3.0f * t2 - 2.0f * t3 );
	}
	return asV;
}

PxF32 sign( PxF32 f )
{
	return f > 0.0f ? 1.0f : ( f < 0.0f ? -1.0f : 0.0f );
}

void unity_tire( const void* shaderData, const PxF32 friction, const PxF32 longSlipIn, const PxF32 latSlipIn, const PxF32 camber, const PxF32 omega,
				 const PxF32 radius, const PxF32 recipRadius, const PxF32 restLoad, const PxF32 normalisedLoad, const PxF32 load, const PxF32 gravity,
				 const PxF32 recipGravity, PxF32& wheelTorque, PxF32& longForce, PxF32& latForce, PxF32& alignMoment )
{
	PX_UNUSED( friction );
	PX_UNUSED( camber );
	PX_UNUSED( omega );
	PX_UNUSED( recipRadius );
	PX_UNUSED( restLoad );
	PX_UNUSED( normalisedLoad );
	PX_UNUSED( gravity );
	PX_UNUSED( recipGravity );
	const TireCurves& t = *static_cast<const TireCurves*>( shaderData );
	PxF32 longSlip = PxAbs( longSlipIn ) >= 1e-5f ? longSlipIn : 0.0f;
	PxF32 latSlip = PxAbs( latSlipIn ) >= 1e-5f ? latSlipIn : 0.0f;
	longForce = longSlip == 0.0f ? 0.0f : sign( longSlip ) * curve( t.forward, longSlip ) * t.forward[4] * load;
	latForce = latSlip == 0.0f ? 0.0f : -sign( latSlip ) * curve( t.sideways, latSlip ) * t.sideways[4] * load;
	PxF32 lf = t.forward[1] * t.forward[4] * load, ls = t.sideways[1] * t.sideways[4] * load;
	if ( lf > 0.0f && ls > 0.0f )
	{
		PxF32 e = ( longForce / lf ) * ( longForce / lf ) + ( latForce / ls ) * ( latForce / ls );
		if ( e > 1.0f )
		{
			PxF32 k = 1.0f / PxSqrt( e );
			longForce *= k;
			latForce *= k;
		}
	}
	wheelTorque = -longForce * radius;
	alignMoment = 0.0f;
}

// Wheel rays: the vehicle's layer mask against the shape's layer, never its own shapes nor triggers.
PxQueryHitType::Enum wheel_filter( PxFilterData query, PxFilterData object, const void*, PxU32, PxHitFlags& )
{
	if ( ( query.word0 & object.word0 ) == 0 || ( object.word3 & 1u ) != 0 )
	{
		return PxQueryHitType::eNONE;
	}
	if ( query.word1 == object.word1 && query.word2 == object.word2 )
	{
		return PxQueryHitType::eNONE;
	}
	return PxQueryHitType::eBLOCK;
}

struct Vehicle
{
	World* world = nullptr;
	PxRigidDynamic* actor = nullptr;
	PxVehicleNoDrive* drive = nullptr;
	PxU32 wheels = 0;
	std::vector<TireCurves> curves;
	std::vector<PxWheelQueryResult> results;
	bool simulated = false;
};

std::unordered_set<Vehicle*> s_vehicles;
PxVehicleDrivableSurfaceToTireFrictionPairs* s_frictionPairs;

Vehicle* vehicle_of( uint64_t id )
{
	Vehicle* v = reinterpret_cast<Vehicle*>( static_cast<uintptr_t>( id ) );
	return s_vehicles.count( v ) ? v : nullptr;
}

// One surface type, one tire type, friction 1: Unity's tires ignore the ground's material.
PxVehicleDrivableSurfaceToTireFrictionPairs* friction_pairs()
{
	if ( s_frictionPairs == nullptr )
	{
		s_frictionPairs = PxVehicleDrivableSurfaceToTireFrictionPairs::allocate( 1, 1 );
		PxVehicleDrivableSurfaceType type;
		type.mType = 0;
		const PxMaterial* none[1] = { nullptr };
		s_frictionPairs->setup( 1, 1, none, &type );
		s_frictionPairs->setTypePairFriction( 0, 0, 1.0f );
	}
	return s_frictionPairs;
}

void update_vehicles( World* world, float dt )
{
	std::vector<PxVehicleWheels*> vehicles;
	std::vector<PxVehicleWheelQueryResult> queries;
	PxU32 rays = 0;
	for ( Vehicle* v : world->vehicles )
	{
		if ( v->drive == nullptr || v->actor->getScene() == nullptr )
		{
			continue;
		}
		vehicles.push_back( v->drive );
		// A sleeping vehicle writes no results: the last ones stay (as Unity reports them).
		if ( v->results.size() != v->wheels )
		{
			v->results.assign( v->wheels, PxWheelQueryResult() );
		}
		PxVehicleWheelQueryResult q;
		q.wheelQueryResults = v->results.data();
		q.nbWheelQueryResults = v->wheels;
		queries.push_back( q );
		rays += v->drive->mWheelsSimData.getNbWheels4() * 4;
	}
	if ( vehicles.empty() )
	{
		return;
	}
	if ( world->wheelResults.size() < rays || world->wheelQuery == nullptr )
	{
		if ( world->wheelQuery != nullptr )
		{
			world->wheelQuery->release();
		}
		world->wheelResults.assign( rays, PxRaycastQueryResult() );
		world->wheelHits.assign( rays, PxRaycastHit() );
		PxBatchQueryDesc desc( rays, 0, 0 );
		desc.queryMemory.userRaycastResultBuffer = world->wheelResults.data();
		desc.queryMemory.userRaycastTouchBuffer = world->wheelHits.data();
		desc.queryMemory.raycastTouchBufferSize = rays;
		desc.preFilterShader = wheel_filter;
		world->wheelQuery = world->scene->createBatchQuery( desc );
	}
	PxVehicleSuspensionRaycasts( world->wheelQuery, static_cast<PxU32>( vehicles.size() ), vehicles.data(), static_cast<PxU32>( world->wheelResults.size() ),
								 world->wheelResults.data() );
	PxVehicleUpdates( dt, world->scene->getGravity(), *friction_pairs(), static_cast<PxU32>( vehicles.size() ), vehicles.data(), queries.data() );
	for ( Vehicle* v : world->vehicles )
	{
		if ( v->drive != nullptr && v->actor->getScene() != nullptr )
		{
			v->simulated = true;
		}
	}
}

void release_vehicle( Vehicle* v )
{
	if ( v->drive != nullptr )
	{
		v->drive->free();
		v->drive = nullptr;
	}
	if ( v->world != nullptr )
	{
		auto& list = v->world->vehicles;
		list.erase( std::remove( list.begin(), list.end(), v ), list.end() );
	}
	s_vehicles.erase( v );
	delete v;
}

// -------------------------------------------------------------------------- character controllers

// What a character controller's move touched, for OnControllerColliderHit.
struct ua3_cct_hit
{
	uint64_t shape;
	ua3_vec3 point;
	ua3_vec3 normal;
	ua3_vec3 direction;
	float length;
};

// A PxCapsuleController with its move's hits and filter: the layers it collides with (as a query
// mask over the shapes' categories), never shapes in an ignored pair with its own.
struct Character : public PxUserControllerHitReport, public PxQueryFilterCallback, public PxControllerFilterCallback
{
	World* world = nullptr;
	PxCapsuleController* controller = nullptr;
	PxShape* shape = nullptr; // its kinematic actor's
	PxU32 mask = 0;
	std::vector<ua3_cct_hit> hits;

	void onShapeHit( const PxControllerShapeHit& hit ) override
	{
		add( hit, hit.shape );
	}
	void onControllerHit( const PxControllersHit& hit ) override;
	void onObstacleHit( const PxControllerObstacleHit& ) override {}

	void add( const PxControllerHit& hit, PxShape* touched )
	{
		ua3_cct_hit h;
		h.shape = pack( touched );
		h.point = PxVec3( static_cast<float>( hit.worldPos.x ), static_cast<float>( hit.worldPos.y ), static_cast<float>( hit.worldPos.z ) );
		h.normal = hit.worldNormal;
		h.direction = hit.dir;
		h.length = hit.length;
		hits.push_back( h );
	}

	bool ignores( const PxShape* other ) const
	{
		return !world->ignores.empty() && world->ignores.count( ordered( shape, const_cast<PxShape*>( other ) ) ) != 0;
	}

	PxQueryHitType::Enum preFilter( const PxFilterData&, const PxShape* other, const PxRigidActor*, PxHitFlags& ) override
	{
		return ignores( other ) ? PxQueryHitType::eNONE : PxQueryHitType::eBLOCK;
	}
	PxQueryHitType::Enum postFilter( const PxFilterData&, const PxQueryHit& ) override
	{
		return PxQueryHitType::eBLOCK;
	}

	// Two controllers collide as their layers do.
	bool filter( const PxController& a, const PxController& b ) override
	{
		const Character* ca = static_cast<const Character*>( a.getUserData() );
		const Character* cb = static_cast<const Character*>( b.getUserData() );
		if ( ca == nullptr || cb == nullptr )
		{
			return true;
		}
		PxFilterData da = ca->shape->getSimulationFilterData(), db = cb->shape->getSimulationFilterData();
		return ( da.word0 & db.word1 ) != 0 && ( db.word0 & da.word1 ) != 0 && !ca->ignores( cb->shape );
	}
};

std::unordered_set<Character*> s_characters;

void Character::onControllerHit( const PxControllersHit& hit )
{
	const Character* other = static_cast<const Character*>( hit.other->getUserData() );
	if ( other != nullptr )
	{
		add( hit, other->shape );
	}
}

Character* character_of( uint64_t id )
{
	Character* c = reinterpret_cast<Character*>( static_cast<uintptr_t>( id ) );
	return s_characters.count( c ) ? c : nullptr;
}

void release_character( Character* c )
{
	if ( c->shape != nullptr )
	{
		forget_shape( c->world, c->shape );
	}
	if ( c->controller != nullptr )
	{
		c->controller->release();
	}
	s_characters.erase( c );
	delete c;
}

void release_controllers( World* world )
{
	for ( auto it = s_characters.begin(); it != s_characters.end(); )
	{
		Character* c = *it;
		++it;
		if ( c->world == world )
		{
			release_character( c );
		}
	}
	if ( world->controllers != nullptr )
	{
		world->controllers->release();
		world->controllers = nullptr;
	}
}
} // namespace

// ----------------------------------------------------------------------------------------- api

UA3_API int ua3_version()
{
	return UA3_VERSION;
}

// Unity's physics settings: default contact offset, sleep threshold, max depenetration velocity
// and angular speed, solver iterations (for bodies created after).
UA3_API void ua3_settings( float contactOffset, float sleepThreshold, float maxDepenetration, float maxAngularVelocity,
						   int positionIterations, int velocityIterations )
{
	s_contactOffset = contactOffset;
	s_sleepThreshold = sleepThreshold;
	s_maxDepenetration = maxDepenetration;
	s_maxAngularVelocity = maxAngularVelocity;
	s_positionIterations = positionIterations;
	s_velocityIterations = velocityIterations;
}

// ----------------------------------------------------------------------------------- world

UA3_API uint32_t ua3_world_create( ua3_vec3 gravityIn, float restitutionThreshold, float hitEventThreshold, int enableSleep )
{
	PxVec3 gravity = gravityIn;
	(void)hitEventThreshold;
	if ( !sdk() )
	{
		return 0;
	}
	World* world = new World();
	world->events.world = world;
	world->filter.world = world;
	world->sleeping = enableSleep != 0;
	PxSceneDesc desc( s_physics->getTolerancesScale() );
	desc.gravity = gravity;
	desc.cpuDispatcher = s_dispatcher;
	desc.filterShader = shader;
	desc.filterCallback = &world->filter;
	desc.simulationEventCallback = &world->events;
	desc.bounceThresholdVelocity = restitutionThreshold;
	desc.frictionType = PxFrictionType::ePATCH;
	desc.solverType = PxSolverType::ePGS;
	desc.broadPhaseType = PxBroadPhaseType::eSAP;
	desc.flags |= PxSceneFlag::eENABLE_CCD | PxSceneFlag::eENABLE_PCM;
	desc.staticKineFilteringMode = PxPairFilteringMode::eKEEP;
	desc.kineKineFilteringMode = PxPairFilteringMode::eKEEP;
	world->scene = s_physics->createScene( desc );
	if ( world->scene == nullptr )
	{
		delete world;
		return 0;
	}
	world->scene->userData = world;
	for ( size_t i = 0; i < s_worlds.size(); ++i )
	{
		if ( s_worlds[i] == nullptr )
		{
			s_worlds[i] = world;
			return static_cast<uint32_t>( i + 1 );
		}
	}
	s_worlds.push_back( world );
	return static_cast<uint32_t>( s_worlds.size() );
}

UA3_API void ua3_world_destroy( uint32_t id )
{
	World* world = world_of( id );
	if ( world == nullptr )
	{
		return;
	}
	while ( !world->vehicles.empty() )
	{
		release_vehicle( world->vehicles.back() );
	}
	release_controllers( world );
	if ( world->wheelQuery != nullptr )
	{
		world->wheelQuery->release();
	}
	for ( auto it = s_joints.begin(); it != s_joints.end(); )
	{
		if ( ( *it )->getScene() == world->scene )
		{
			( *it )->release();
			it = s_joints.erase( it );
		}
		else
		{
			++it;
		}
	}
	PxU32 count = world->scene->getNbActors( PxActorTypeFlag::eRIGID_STATIC | PxActorTypeFlag::eRIGID_DYNAMIC );
	std::vector<PxActor*> actors( count );
	world->scene->getActors( PxActorTypeFlag::eRIGID_STATIC | PxActorTypeFlag::eRIGID_DYNAMIC, actors.data(), count );
	for ( PxActor* actor : actors )
	{
		PxRigidActor* rigid = actor->is<PxRigidActor>();
		if ( rigid == nullptr )
		{
			continue;
		}
		std::vector<PxShape*> shapes( rigid->getNbShapes() );
		rigid->getShapes( shapes.data(), static_cast<PxU32>( shapes.size() ) );
		for ( PxShape* shape : shapes )
		{
			forget_shape( nullptr, shape );
		}
		s_bodies.erase( rigid );
		rigid->release();
	}
	world->scene->release();
	s_worlds[id - 1] = nullptr;
	delete world;
}

UA3_API void ua3_world_step( uint32_t id, float timeStep, int subStepCount )
{
	(void)subStepCount;
	World* world = world_of( id );
	if ( world == nullptr )
	{
		return;
	}
	for ( auto& list : world->lists )
	{
		list.clear();
	}
	world->contacts.clear();
	world->broken.clear();
	update_vehicles( world, timeStep );
	world->scene->simulate( timeStep );
	world->scene->fetchResults( true );
}

UA3_API void ua3_world_set_gravity( uint32_t id, ua3_vec3 gravityIn )
{
	PxVec3 gravity = gravityIn;
	if ( World* world = world_of( id ) )
	{
		world->scene->setGravity( gravity );
	}
}

UA3_API void ua3_world_set_restitution_threshold( uint32_t id, float value )
{
	if ( World* world = world_of( id ) )
	{
		world->scene->setBounceThresholdVelocity( value );
	}
}

UA3_API void ua3_world_enable_sleeping( uint32_t id, int flag )
{
	if ( World* world = world_of( id ) )
	{
		world->sleeping = flag != 0;
	}
}

// The begin/end events of the last step: kind 0 contact begin, 1 contact end, 2 sensor begin
// (a = sensor), 3 sensor end. Returns the number of events; at most capacity are written.
UA3_API int ua3_world_events( uint32_t id, int kind, ua3_pair* out, int capacity )
{
	World* world = world_of( id );
	if ( world == nullptr || kind < 0 || kind > 3 )
	{
		return 0;
	}
	const std::vector<ua3_pair>& list = world->lists[kind];
	int count = static_cast<int>( list.size() );
	for ( int i = 0; i < count && i < capacity; ++i )
	{
		out[i] = list[i];
	}
	return count;
}

// Physics.IgnoreCollision between two shapes.
UA3_API void ua3_ignore_pair( uint32_t id, uint64_t a, uint64_t b, int ignore )
{
	World* world = world_of( id );
	PxShape* sa = shape_of( a );
	PxShape* sb = shape_of( b );
	if ( world == nullptr || sa == nullptr || sb == nullptr )
	{
		return;
	}
	auto key = ordered( sa, sb );
	bool present = world->ignores.count( key ) != 0;
	if ( ( ignore != 0 ) == present )
	{
		return;
	}
	if ( ignore )
	{
		world->ignores.insert( key );
		++info( sa )->ignores;
		++info( sb )->ignores;
	}
	else
	{
		world->ignores.erase( key );
		--info( sa )->ignores;
		--info( sb )->ignores;
	}
	set_filter_flags( sa );
	set_filter_flags( sb );
	refilter( sa );
	refilter( sb );
}

// ------------------------------------------------------------------------------------ bodies

// Motion lock bits: 1 linear x, 2 linear y, 4 linear z, 8 angular x, 16 angular y, 32 angular z
// (PhysX's PxRigidDynamicLockFlag bits).
struct ua3_body_def
{
	int type; // 0 static, 1 kinematic, 2 dynamic
	PxVec3 position;
	PxQuat rotation;
	PxVec3 linearVelocity;
	PxVec3 angularVelocity;
	float linearDamping;
	float angularDamping;
	float gravityScale;
	int locks;
	int enableSleep;
	int isAwake;
	int isBullet;
};

UA3_API uint64_t ua3_body_create( uint32_t id, const ua3_body_def* def )
{
	World* world = world_of( id );
	if ( world == nullptr )
	{
		return 0;
	}
	PxTransform pose( def->position, def->rotation.getNormalized() );
	PxRigidActor* actor;
	if ( def->type == 0 )
	{
		actor = s_physics->createRigidStatic( pose );
	}
	else
	{
		PxRigidDynamic* body = s_physics->createRigidDynamic( pose );
		body->setRigidBodyFlag( PxRigidBodyFlag::eKINEMATIC, def->type == 1 );
		body->setLinearDamping( def->linearDamping );
		body->setAngularDamping( def->angularDamping );
		body->setActorFlag( PxActorFlag::eDISABLE_GRAVITY, def->gravityScale == 0.0f );
		body->setRigidDynamicLockFlags( PxRigidDynamicLockFlags( static_cast<PxU8>( def->locks & 63 ) ) );
		body->setSolverIterationCounts( static_cast<PxU32>( s_positionIterations ), static_cast<PxU32>( s_velocityIterations ) );
		body->setSleepThreshold( def->enableSleep && world->sleeping ? s_sleepThreshold : 0.0f );
		body->setMaxDepenetrationVelocity( s_maxDepenetration );
		body->setMaxAngularVelocity( s_maxAngularVelocity );
		body->setRigidBodyFlag( PxRigidBodyFlag::eENABLE_CCD, def->isBullet != 0 );
		if ( def->type == 2 )
		{
			body->setLinearVelocity( def->linearVelocity, false );
			body->setAngularVelocity( def->angularVelocity, false );
		}
		actor = body;
	}
	world->scene->addActor( *actor );
	if ( PxRigidDynamic* body = actor->is<PxRigidDynamic>() )
	{
		if ( !def->isAwake && def->type == 2 )
		{
			body->putToSleep();
		}
	}
	s_bodies.insert( actor );
	return pack( actor );
}

UA3_API void ua3_body_destroy( uint64_t id )
{
	PxRigidActor* actor = body_of( id );
	if ( actor == nullptr )
	{
		return;
	}
	World* world = world_of_actor( actor );
	if ( world != nullptr )
	{
		std::vector<Vehicle*> on( world->vehicles );
		for ( Vehicle* v : on )
		{
			if ( v->actor == actor )
			{
				release_vehicle( v );
			}
		}
	}
	std::vector<PxShape*> shapes( actor->getNbShapes() );
	actor->getShapes( shapes.data(), static_cast<PxU32>( shapes.size() ) );
	for ( PxShape* shape : shapes )
	{
		forget_shape( world, shape );
	}
	s_bodies.erase( actor );
	actor->release();
}

UA3_API int ua3_body_is_valid( uint64_t id )
{
	return body_of( id ) != nullptr;
}

UA3_API void ua3_body_set_type( uint64_t id, int type )
{
	if ( PxRigidDynamic* body = dynamic_of( id ) )
	{
		body->setRigidBodyFlag( PxRigidBodyFlag::eKINEMATIC, type == 1 );
	}
}

UA3_API void ua3_body_get_transform( uint64_t id, PxVec3* position, PxQuat* rotation )
{
	PxRigidActor* actor = body_of( id );
	PxTransform pose = actor != nullptr ? actor->getGlobalPose() : PxTransform( PxIdentity );
	*position = pose.p;
	*rotation = pose.q;
}

UA3_API void ua3_body_set_transform( uint64_t id, ua3_vec3 positionIn, ua3_quat rotationIn )
{
	PxVec3 position = positionIn;
	PxQuat rotation = rotationIn;
	if ( PxRigidActor* actor = body_of( id ) )
	{
		actor->setGlobalPose( PxTransform( position, rotation.getNormalized() ) );
	}
}

// A body's pose and linear velocity, as ua3_bodies_state reads them.
struct ua3_body_state
{
	ua3_vec3 position;
	ua3_quat rotation;
	ua3_vec3 linear_velocity;
};
static_assert( sizeof( ua3_body_state ) == 40, "ua3_body_state is 40 bytes" );

// The pose and linear velocity of many bodies in one call (around a step); an id that names no
// body reads as the identity pose at rest.
UA3_API void ua3_bodies_state( const uint64_t* ids, int count, ua3_body_state* output )
{
	for ( int i = 0; i < count; i++ )
	{
		PxRigidActor* actor = body_of( ids[i] );
		PxTransform pose = actor != nullptr ? actor->getGlobalPose() : PxTransform( PxIdentity );
		PxRigidDynamic* dynamic = actor != nullptr ? actor->is<PxRigidDynamic>() : nullptr;
		output[i].position = pose.p;
		output[i].rotation = { pose.q.x, pose.q.y, pose.q.z, pose.q.w };
		output[i].linear_velocity = dynamic != nullptr ? dynamic->getLinearVelocity() : PxVec3( 0.0f );
	}
}

// Moves a kinematic body to a pose over the next step (Rigidbody.MovePosition/MoveRotation).
UA3_API void ua3_body_set_target_transform( uint64_t id, ua3_vec3 positionIn, ua3_quat rotationIn, float timeStep )
{
	PxVec3 position = positionIn;
	PxQuat rotation = rotationIn;
	(void)timeStep;
	PxRigidDynamic* body = dynamic_of( id );
	if ( body == nullptr )
	{
		return;
	}
	PxTransform target( position, rotation.getNormalized() );
	if ( body->getRigidBodyFlags() & PxRigidBodyFlag::eKINEMATIC )
	{
		body->setKinematicTarget( target );
	}
	else
	{
		body->setGlobalPose( target );
	}
}

UA3_API ua3_vec3 ua3_body_get_linear_velocity( uint64_t id )
{
	PxRigidDynamic* body = dynamic_of( id );
	return body != nullptr ? body->getLinearVelocity() : PxVec3( 0.0f );
}

UA3_API void ua3_body_set_linear_velocity( uint64_t id, ua3_vec3 vIn )
{
	PxVec3 v = vIn;
	PxRigidDynamic* body = dynamic_of( id );
	if ( body != nullptr && !( body->getRigidBodyFlags() & PxRigidBodyFlag::eKINEMATIC ) )
	{
		body->setLinearVelocity( v, !v.isZero() );
	}
}

UA3_API ua3_vec3 ua3_body_get_angular_velocity( uint64_t id )
{
	PxRigidDynamic* body = dynamic_of( id );
	return body != nullptr ? body->getAngularVelocity() : PxVec3( 0.0f );
}

UA3_API void ua3_body_set_angular_velocity( uint64_t id, ua3_vec3 vIn )
{
	PxVec3 v = vIn;
	PxRigidDynamic* body = dynamic_of( id );
	if ( body != nullptr && !( body->getRigidBodyFlags() & PxRigidBodyFlag::eKINEMATIC ) )
	{
		body->setAngularVelocity( v, !v.isZero() );
	}
}

UA3_API ua3_vec3 ua3_body_get_point_velocity( uint64_t id, ua3_vec3 worldPointIn )
{
	PxVec3 worldPoint = worldPointIn;
	PxRigidDynamic* body = dynamic_of( id );
	return body != nullptr ? PxRigidBodyExt::getVelocityAtPos( *body, worldPoint ) : PxVec3( 0.0f );
}

UA3_API ua3_vec3 ua3_body_get_world_center( uint64_t id )
{
	PxRigidActor* actor = body_of( id );
	if ( actor == nullptr )
	{
		return PxVec3( 0.0f );
	}
	if ( PxRigidBody* body = actor->is<PxRigidBody>() )
	{
		return actor->getGlobalPose().transform( body->getCMassLocalPose().p );
	}
	return actor->getGlobalPose().p;
}

// kind: 0 force at point, 1 force at center, 2 torque, 3 impulse at point, 4 impulse at center,
// 5 angular impulse.
UA3_API void ua3_body_apply( uint64_t id, int kind, ua3_vec3 vectorIn, ua3_vec3 pointIn )
{
	PxVec3 vector = vectorIn;
	PxVec3 point = pointIn;
	PxRigidDynamic* body = dynamic_of( id );
	if ( body == nullptr || ( body->getRigidBodyFlags() & PxRigidBodyFlag::eKINEMATIC ) )
	{
		return;
	}
	switch ( kind )
	{
		case 0:
			PxRigidBodyExt::addForceAtPos( *body, vector, point, PxForceMode::eFORCE );
			break;
		case 1:
			body->addForce( vector, PxForceMode::eFORCE );
			break;
		case 2:
			body->addTorque( vector, PxForceMode::eFORCE );
			break;
		case 3:
			PxRigidBodyExt::addForceAtPos( *body, vector, point, PxForceMode::eIMPULSE );
			break;
		case 4:
			body->addForce( vector, PxForceMode::eIMPULSE );
			break;
		default:
			body->addTorque( vector, PxForceMode::eIMPULSE );
			break;
	}
}

// The mass of the body's simulation shapes at their densities: mass, local center of mass and the
// rotational inertia about that center (columns of a 3x3 matrix, in the body frame).
UA3_API void ua3_body_shape_mass( uint64_t id, float* mass, PxVec3* center, PxMat33* inertia )
{
	*mass = 0.0f;
	*center = PxVec3( 0.0f );
	*inertia = PxMat33( PxZero );
	PxRigidDynamic* body = dynamic_of( id );
	if ( body == nullptr )
	{
		return;
	}
	std::vector<PxShape*> shapes( body->getNbShapes() );
	body->getShapes( shapes.data(), static_cast<PxU32>( shapes.size() ) );
	std::vector<PxShape*> simulated;
	std::vector<PxReal> densities;
	for ( PxShape* shape : shapes )
	{
		if ( shape->getFlags() & PxShapeFlag::eSIMULATION_SHAPE )
		{
			simulated.push_back( shape );
			densities.push_back( info( shape ) != nullptr ? info( shape )->density : 1.0f );
		}
	}
	if ( simulated.empty() )
	{
		return;
	}
	// Each shape's mass properties at its own density, summed (the inertia about the common
	// center of mass).
	std::vector<PxMassProperties> parts;
	std::vector<PxTransform> poses;
	for ( size_t i = 0; i < simulated.size(); ++i )
	{
		parts.push_back( PxRigidBodyExt::computeMassPropertiesFromShapes( &simulated[i], 1 ) * densities[i] );
		poses.push_back( PxTransform( PxIdentity ) );
	}
	PxMassProperties properties = PxMassProperties::sum( parts.data(), poses.data(), static_cast<PxU32>( parts.size() ) );
	*mass = properties.mass;
	*center = properties.centerOfMass;
	*inertia = properties.inertiaTensor;
}

UA3_API void ua3_body_set_mass( uint64_t id, float mass, ua3_vec3 centerIn, const PxMat33* inertia )
{
	PxVec3 center = centerIn;
	PxRigidDynamic* body = dynamic_of( id );
	if ( body == nullptr )
	{
		return;
	}
	PxQuat frame;
	PxVec3 diagonal = PxDiagonalize( *inertia, frame );
	body->setMass( mass );
	body->setCMassLocalPose( PxTransform( center, frame ) );
	body->setMassSpaceInertiaTensor( PxVec3( PxMax( diagonal.x, 0.0f ), PxMax( diagonal.y, 0.0f ), PxMax( diagonal.z, 0.0f ) ) );
}

UA3_API void ua3_body_set_damping( uint64_t id, float linear, float angular )
{
	if ( PxRigidDynamic* body = dynamic_of( id ) )
	{
		body->setLinearDamping( linear );
		body->setAngularDamping( angular );
	}
}

UA3_API void ua3_body_set_gravity_scale( uint64_t id, float scale )
{
	if ( PxRigidDynamic* body = dynamic_of( id ) )
	{
		body->setActorFlag( PxActorFlag::eDISABLE_GRAVITY, scale == 0.0f );
	}
}

UA3_API int ua3_body_is_awake( uint64_t id )
{
	PxRigidDynamic* body = dynamic_of( id );
	return body != nullptr && body->getScene() != nullptr && !body->isSleeping();
}

UA3_API void ua3_body_set_awake( uint64_t id, int awake )
{
	PxRigidDynamic* body = dynamic_of( id );
	if ( body == nullptr || body->getScene() == nullptr )
	{
		return;
	}
	if ( awake )
	{
		body->wakeUp();
	}
	else if ( !( body->getRigidBodyFlags() & PxRigidBodyFlag::eKINEMATIC ) )
	{
		body->putToSleep();
	}
}

UA3_API void ua3_body_enable_sleep( uint64_t id, int flag )
{
	if ( PxRigidDynamic* body = dynamic_of( id ) )
	{
		body->setSleepThreshold( flag ? s_sleepThreshold : 0.0f );
	}
}

UA3_API void ua3_body_set_sleep_threshold( uint64_t id, float threshold )
{
	if ( PxRigidDynamic* body = dynamic_of( id ) )
	{
		body->setSleepThreshold( threshold );
	}
}

UA3_API void ua3_body_set_locks( uint64_t id, int locks )
{
	if ( PxRigidDynamic* body = dynamic_of( id ) )
	{
		body->setRigidDynamicLockFlags( PxRigidDynamicLockFlags( static_cast<PxU8>( locks & 63 ) ) );
	}
}

UA3_API void ua3_body_set_bullet( uint64_t id, int flag )
{
	if ( PxRigidDynamic* body = dynamic_of( id ) )
	{
		body->setRigidBodyFlag( PxRigidBodyFlag::eENABLE_CCD, flag != 0 );
	}
}

UA3_API void ua3_body_set_enabled( uint64_t id, int enabled )
{
	if ( PxRigidActor* actor = body_of( id ) )
	{
		actor->setActorFlag( PxActorFlag::eDISABLE_SIMULATION, enabled == 0 );
	}
}

UA3_API void ua3_body_set_max_angular_velocity( uint64_t id, float value )
{
	if ( PxRigidDynamic* body = dynamic_of( id ) )
	{
		body->setMaxAngularVelocity( value );
	}
}

UA3_API void ua3_body_set_max_depenetration( uint64_t id, float value )
{
	if ( PxRigidDynamic* body = dynamic_of( id ) )
	{
		body->setMaxDepenetrationVelocity( value );
	}
}

UA3_API void ua3_body_set_solver_iterations( uint64_t id, int position, int velocity )
{
	if ( PxRigidDynamic* body = dynamic_of( id ) )
	{
		body->setSolverIterationCounts( static_cast<PxU32>( PxMax( position, 1 ) ), static_cast<PxU32>( PxMax( velocity, 0 ) ) );
	}
}

// ------------------------------------------------------------------------------------- shapes

struct ua3_shape_def
{
	float density;
	float friction; // dynamic
	float restitution;
	uint64_t material; // combine modes: bits 0-1 friction, 2-3 bounce
	uint64_t categoryBits;
	uint64_t maskBits;
	int isSensor;
	int enableSensorEvents;
	int enableContactEvents;
	int updateBodyMass;
	float staticFriction;
	float contactOffset;
};

namespace
{
// A shape's query filter data: word0 its layer's bit (scene queries test it against their mask),
// word1/word2 its actor (wheel rays skip their own vehicle's shapes), word3 1 for triggers.
PxFilterData query_data( PxU32 category, const PxRigidActor* actor, bool trigger )
{
	uint64_t a = pack( actor );
	return PxFilterData( category, static_cast<PxU32>( a ), static_cast<PxU32>( a >> 32 ), trigger ? 1u : 0u );
}

uint64_t attach( uint64_t bodyId, const ua3_shape_def* def, const PxGeometry& geometry, const PxTransform& local )
{
	PxRigidActor* actor = body_of( bodyId );
	if ( actor == nullptr )
	{
		return 0;
	}
	PxShapeFlags flags = PxShapeFlag::eSCENE_QUERY_SHAPE | PxShapeFlag::eVISUALIZATION;
	flags |= def->isSensor ? PxShapeFlag::eTRIGGER_SHAPE : PxShapeFlag::eSIMULATION_SHAPE;
	PxMaterial* m = material( def->staticFriction, def->friction, def->restitution, def->material );
	PxShape* shape = s_physics->createShape( geometry, *m, true, flags );
	if ( shape == nullptr )
	{
		return 0;
	}
	shape->setLocalPose( local );
	shape->setContactOffset( def->contactOffset > 0.0f ? def->contactOffset : s_contactOffset );
	shape->setRestOffset( 0.0f );
	ShapeInfo* i = new ShapeInfo{ def->density, def->enableContactEvents != 0, def->enableSensorEvents != 0, 0 };
	shape->userData = i;
	PxFilterData simulation( static_cast<PxU32>( def->categoryBits ), static_cast<PxU32>( def->maskBits ), 0, 0 );
	shape->setSimulationFilterData( simulation );
	set_filter_flags( shape );
	shape->setQueryFilterData( query_data( static_cast<PxU32>( def->categoryBits ), actor, def->isSensor != 0 ) );
	actor->attachShape( *shape );
	shape->release(); // the actor holds it
	s_shapes.insert( shape );
	return pack( shape );
}

// The rotation that turns PhysX's capsule axis (x) onto a direction.
PxQuat axis_rotation( const PxVec3& direction )
{
	PxVec3 d = direction.getNormalized();
	PxVec3 x( 1.0f, 0.0f, 0.0f );
	float c = x.dot( d );
	if ( c > 0.999999f )
	{
		return PxQuat( PxIdentity );
	}
	if ( c < -0.999999f )
	{
		return PxQuat( PxPi, PxVec3( 0.0f, 1.0f, 0.0f ) );
	}
	PxVec3 axis = x.cross( d );
	float s = PxSqrt( ( 1.0f + c ) * 2.0f );
	return PxQuat( axis.x / s, axis.y / s, axis.z / s, s * 0.5f ).getNormalized();
}
} // namespace

UA3_API uint64_t ua3_shape_sphere( uint64_t body, const ua3_shape_def* def, ua3_vec3 centerIn, float radius )
{
	PxVec3 center = centerIn;
	return attach( body, def, PxSphereGeometry( PxMax( radius, 1e-5f ) ), PxTransform( center ) );
}

UA3_API uint64_t ua3_shape_capsule( uint64_t body, const ua3_shape_def* def, ua3_vec3 center1In, ua3_vec3 center2In, float radius )
{
	PxVec3 center1 = center1In;
	PxVec3 center2 = center2In;
	PxVec3 axis = center2 - center1;
	float half = axis.magnitude() * 0.5f;
	if ( half < 1e-6f )
	{
		return attach( body, def, PxSphereGeometry( PxMax( radius, 1e-5f ) ), PxTransform( ( center1 + center2 ) * 0.5f ) );
	}
	return attach( body, def, PxCapsuleGeometry( PxMax( radius, 1e-5f ), half ), PxTransform( ( center1 + center2 ) * 0.5f, axis_rotation( axis ) ) );
}

UA3_API uint64_t ua3_shape_box( uint64_t body, const ua3_shape_def* def, ua3_vec3 centerIn, ua3_quat rotationIn, ua3_vec3 halfExtentsIn )
{
	PxVec3 center = centerIn;
	PxQuat rotation = rotationIn;
	PxVec3 halfExtents = halfExtentsIn;
	PxVec3 h( PxMax( halfExtents.x, 1e-5f ), PxMax( halfExtents.y, 1e-5f ), PxMax( halfExtents.z, 1e-5f ) );
	return attach( body, def, PxBoxGeometry( h ), PxTransform( center, rotation.getNormalized() ) );
}

// A convex hull of points in the body frame (cooked as Unity cooks convex MeshColliders); 0 when
// they make no hull.
UA3_API uint64_t ua3_shape_hull( uint64_t body, const ua3_shape_def* def, const PxVec3* points, int count )
{
	if ( s_cooking == nullptr || count < 4 )
	{
		return 0;
	}
	PxConvexMeshDesc desc;
	desc.points.count = static_cast<PxU32>( count );
	desc.points.stride = sizeof( PxVec3 );
	desc.points.data = points;
	desc.flags = PxConvexFlag::eCOMPUTE_CONVEX | PxConvexFlag::eSHIFT_VERTICES;
	desc.vertexLimit = 255;
	PxConvexMesh* mesh = s_cooking->createConvexMesh( desc, s_physics->getPhysicsInsertionCallback() );
	if ( mesh == nullptr )
	{
		return 0;
	}
	uint64_t shape = attach( body, def, PxConvexMeshGeometry( mesh ), PxTransform( PxIdentity ) );
	mesh->release(); // the shape holds it
	return shape;
}

// Triangle mesh data (Unity's winding: the cross product of a front face's edges points out, as
// in PhysX); released when the caller and every shape using it let go.
UA3_API void* ua3_mesh_create( const PxVec3* vertices, int vertexCount, const int32_t* indices, int triangleCount )
{
	if ( s_cooking == nullptr && !sdk() )
	{
		return nullptr;
	}
	PxTriangleMeshDesc desc;
	desc.points.count = static_cast<PxU32>( vertexCount );
	desc.points.stride = sizeof( PxVec3 );
	desc.points.data = vertices;
	desc.triangles.count = static_cast<PxU32>( triangleCount );
	desc.triangles.stride = 3 * sizeof( int32_t );
	desc.triangles.data = indices;
	return s_cooking->createTriangleMesh( desc, s_physics->getPhysicsInsertionCallback() );
}

UA3_API void ua3_mesh_destroy( void* mesh )
{
	if ( mesh != nullptr )
	{
		static_cast<PxTriangleMesh*>( mesh )->release();
	}
}

UA3_API uint64_t ua3_shape_mesh( uint64_t body, const ua3_shape_def* def, const void* mesh, ua3_vec3 scaleIn )
{
	PxVec3 scale = scaleIn;
	if ( mesh == nullptr )
	{
		return 0;
	}
	PxTriangleMesh* m = const_cast<PxTriangleMesh*>( static_cast<const PxTriangleMesh*>( mesh ) );
	return attach( body, def, PxTriangleMeshGeometry( m, PxMeshScale( scale ) ), PxTransform( PxIdentity ) );
}

// A terrain's height field as Unity builds it: rows along x, columns along z, heights as Unity's
// 16-bit samples, a hole flag per cell, tessellation flag per sample.
UA3_API void* ua3_height_field_samples( const int16_t* heights, const uint8_t* holes, int rows, int columns, int tessellate )
{
	if ( !sdk() || rows < 2 || columns < 2 )
	{
		return nullptr;
	}
	std::vector<PxHeightFieldSample> samples( static_cast<size_t>( rows ) * columns );
	for ( int r = 0; r < rows; ++r )
	{
		for ( int c = 0; c < columns; ++c )
		{
			PxHeightFieldSample& s = samples[static_cast<size_t>( r ) * columns + c];
			s.height = heights[static_cast<size_t>( r ) * columns + c];
			bool hole = r < rows - 1 && c < columns - 1 && holes != nullptr && holes[static_cast<size_t>( r ) * ( columns - 1 ) + c] != 0;
			s.materialIndex0 = hole ? PxHeightFieldMaterial::eHOLE : 0;
			s.materialIndex1 = hole ? PxHeightFieldMaterial::eHOLE : 0;
			if ( tessellate )
			{
				s.setTessFlag();
			}
		}
	}
	PxHeightFieldDesc desc;
	desc.format = PxHeightFieldFormat::eS16_TM;
	desc.nbRows = static_cast<PxU32>( rows );
	desc.nbColumns = static_cast<PxU32>( columns );
	desc.samples.data = samples.data();
	desc.samples.stride = sizeof( PxHeightFieldSample );
	return s_cooking->createHeightField( desc, s_physics->getPhysicsInsertionCallback() );
}

UA3_API void ua3_height_field_destroy( void* heightField )
{
	if ( heightField != nullptr )
	{
		static_cast<PxHeightField*>( heightField )->release();
	}
}

UA3_API uint64_t ua3_shape_height_field_scaled( uint64_t body, const ua3_shape_def* def, const void* heightField, float heightScale,
												float rowScale, float columnScale, ua3_vec3 offsetIn )
{
	PxVec3 offset = offsetIn;
	if ( heightField == nullptr )
	{
		return 0;
	}
	PxHeightField* hf = const_cast<PxHeightField*>( static_cast<const PxHeightField*>( heightField ) );
	PxHeightFieldGeometry geometry( hf, PxMeshGeometryFlags(), heightScale, rowScale, columnScale );
	return attach( body, def, geometry, PxTransform( offset ) );
}

UA3_API void ua3_shape_destroy( uint64_t id, int updateBodyMass )
{
	(void)updateBodyMass;
	PxShape* shape = shape_of( id );
	if ( shape == nullptr )
	{
		return;
	}
	PxRigidActor* actor = shape->getActor();
	forget_shape( world_of_actor( actor ), shape );
	if ( actor != nullptr )
	{
		actor->detachShape( *shape );
	}
}

UA3_API int ua3_shape_is_valid( uint64_t id )
{
	return shape_of( id ) != nullptr;
}

UA3_API int ua3_shape_is_sensor( uint64_t id )
{
	PxShape* shape = shape_of( id );
	return shape != nullptr && ( shape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE );
}

UA3_API uint64_t ua3_shape_body( uint64_t id )
{
	PxShape* shape = shape_of( id );
	return shape != nullptr ? pack( shape->getActor() ) : 0;
}

UA3_API void ua3_shape_set_material( uint64_t id, float friction, float restitution, uint64_t modes, float staticFriction )
{
	if ( PxShape* shape = shape_of( id ) )
	{
		PxMaterial* m = material( staticFriction, friction, restitution, modes );
		shape->setMaterials( &m, 1 );
	}
}

UA3_API void ua3_shape_set_filter( uint64_t id, uint64_t categoryBits, uint64_t maskBits )
{
	PxShape* shape = shape_of( id );
	if ( shape == nullptr )
	{
		return;
	}
	PxFilterData data = shape->getSimulationFilterData();
	data.word0 = static_cast<PxU32>( categoryBits );
	data.word1 = static_cast<PxU32>( maskBits );
	shape->setSimulationFilterData( data );
	shape->setQueryFilterData( query_data( static_cast<PxU32>( categoryBits ), shape->getActor(), ( shape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE ) ) );
	refilter( shape );
}

UA3_API void ua3_shape_set_density( uint64_t id, float density )
{
	if ( PxShape* shape = shape_of( id ) )
	{
		info( shape )->density = density;
	}
}

struct ua3_bounds
{
	PxVec3 lower, upper;
};

UA3_API void ua3_shape_aabb( uint64_t id, ua3_bounds* bounds )
{
	PxShape* shape = shape_of( id );
	if ( shape == nullptr || shape->getActor() == nullptr )
	{
		*bounds = { PxVec3( 0.0f ), PxVec3( 0.0f ) };
		return;
	}
	PxBounds3 b = PxShapeExt::getWorldBounds( *shape, *shape->getActor(), 1.0f );
	*bounds = { b.minimum, b.maximum };
}

UA3_API ua3_vec3 ua3_shape_closest_point( uint64_t id, ua3_vec3 targetIn )
{
	PxVec3 target = targetIn;
	PxShape* shape = shape_of( id );
	if ( shape == nullptr || shape->getActor() == nullptr )
	{
		return target;
	}
	PxGeometryHolder geometry = shape->getGeometry();
	PxGeometryType::Enum type = geometry.getType();
	if ( type != PxGeometryType::eSPHERE && type != PxGeometryType::eCAPSULE && type != PxGeometryType::eBOX && type != PxGeometryType::eCONVEXMESH )
	{
		return target;
	}
	PxVec3 closest;
	PxReal d = PxGeometryQuery::pointDistance( target, geometry.any(), PxShapeExt::getGlobalPose( *shape, *shape->getActor() ), &closest );
	return d > 0.0f ? closest : target;
}

struct ua3_hit
{
	uint64_t shape;
	PxVec3 point;
	PxVec3 normal;
	float fraction;
	int triangleIndex;
};

UA3_API int ua3_shape_raycast( uint64_t id, ua3_vec3 originIn, ua3_vec3 translationIn, ua3_hit* hit )
{
	PxVec3 origin = originIn;
	PxVec3 translation = translationIn;
	PxShape* shape = shape_of( id );
	float length = translation.magnitude();
	if ( shape == nullptr || shape->getActor() == nullptr || length <= 0.0f )
	{
		return 0;
	}
	PxRaycastHit result;
	PxU32 n = PxGeometryQuery::raycast( origin, translation / length, shape->getGeometry().any(), PxShapeExt::getGlobalPose( *shape, *shape->getActor() ),
										length, PxHitFlag::ePOSITION | PxHitFlag::eNORMAL | PxHitFlag::eFACE_INDEX, 1, &result );
	if ( n == 0 )
	{
		return 0;
	}
	hit->shape = id;
	hit->point = result.position;
	hit->normal = result.normal;
	hit->fraction = result.distance / length;
	hit->triangleIndex = static_cast<int>( result.faceIndex );
	return 1;
}

// The touching contact points of a shape in the last step. Returns the number of points; at most
// capacity are written.
UA3_API int ua3_shape_contacts( uint64_t id, ua3_contact* out, int capacity )
{
	PxShape* shape = shape_of( id );
	World* world = shape != nullptr ? world_of_actor( shape->getActor() ) : nullptr;
	if ( world == nullptr )
	{
		return 0;
	}
	int count = 0;
	for ( const auto& entry : world->contacts )
	{
		if ( entry.first.first != shape && entry.first.second != shape )
		{
			continue;
		}
		for ( const ua3_contact& c : entry.second )
		{
			if ( count < capacity )
			{
				out[count] = c;
			}
			++count;
		}
	}
	return count;
}

// ------------------------------------------------------------------------------------ queries

namespace
{
class QueryFilter : public PxQueryFilterCallback
{
public:
	bool hitTriggers = false;
	PxQueryHitType::Enum preFilter( const PxFilterData&, const PxShape* shape, const PxRigidActor*, PxHitFlags& ) override
	{
		if ( !hitTriggers && ( shape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE ) )
		{
			return PxQueryHitType::eNONE;
		}
		return PxQueryHitType::eTOUCH;
	}
	PxQueryHitType::Enum postFilter( const PxFilterData&, const PxQueryHit& ) override
	{
		return PxQueryHitType::eTOUCH;
	}
};

PxQueryFilterData query_filter( uint64_t maskBits )
{
	PxQueryFilterData data( PxFilterData( static_cast<PxU32>( maskBits ), 0, 0, 0 ),
							PxQueryFlag::eSTATIC | PxQueryFlag::eDYNAMIC | PxQueryFlag::ePREFILTER | PxQueryFlag::eNO_BLOCK );
	return data;
}

// Writes touches as hits; the closest (fraction > 0: Unity's single casts miss the shapes they
// start in) when closest is set.
template <typename Hit>
int collect( const Hit* touches, PxU32 count, float length, int closest, ua3_hit* hits, int capacity )
{
	if ( closest )
	{
		int best = -1;
		for ( PxU32 i = 0; i < count; ++i )
		{
			if ( touches[i].distance <= 0.0f )
			{
				continue;
			}
			if ( best < 0 || touches[i].distance < touches[best].distance )
			{
				best = static_cast<int>( i );
			}
		}
		if ( best < 0 || capacity < 1 )
		{
			return best < 0 ? 0 : 1;
		}
		const Hit& t = touches[best];
		hits[0] = { pack( t.shape ), t.position, t.normal, length > 0.0f ? t.distance / length : 0.0f, static_cast<int>( t.faceIndex ) };
		return 1;
	}
	for ( PxU32 i = 0; i < count && static_cast<int>( i ) < capacity; ++i )
	{
		const Hit& t = touches[i];
		hits[i] = { pack( t.shape ), t.position, t.normal, length > 0.0f ? t.distance / length : 0.0f, static_cast<int>( t.faceIndex ) };
	}
	return static_cast<int>( count );
}

// The query shapes: kind 0 sphere (a = center, radius), 1 capsule (a, b = centers, radius),
// 2 box (a = center, b = half extents, rotation).
PxGeometryHolder query_geometry( int kind, PxVec3 a, PxVec3 b, PxQuat rotation, float radius, PxTransform& pose )
{
	PxGeometryHolder holder;
	if ( kind == 0 )
	{
		holder.storeAny( PxSphereGeometry( PxMax( radius, 1e-5f ) ) );
		pose = PxTransform( a );
	}
	else if ( kind == 1 )
	{
		PxVec3 axis = b - a;
		float half = axis.magnitude() * 0.5f;
		if ( half < 1e-6f )
		{
			holder.storeAny( PxSphereGeometry( PxMax( radius, 1e-5f ) ) );
			pose = PxTransform( ( a + b ) * 0.5f );
		}
		else
		{
			holder.storeAny( PxCapsuleGeometry( PxMax( radius, 1e-5f ), half ) );
			pose = PxTransform( ( a + b ) * 0.5f, axis_rotation( axis ) );
		}
	}
	else
	{
		holder.storeAny( PxBoxGeometry( PxMax( b.x, 1e-5f ), PxMax( b.y, 1e-5f ), PxMax( b.z, 1e-5f ) ) );
		pose = PxTransform( a, rotation.getNormalized() );
	}
	return holder;
}

const PxU32 MaxTouches = 4096;
} // namespace

// Ray casts: the closest hit (closest != 0, one hit written) or all hits along the ray. Returns
// the number of hits; at most capacity are written.
UA3_API int ua3_raycast( uint32_t id, ua3_vec3 originIn, ua3_vec3 translationIn, uint64_t maskBits, int hitTriggers, int closest, ua3_hit* hits,
						 int capacity )
{
	PxVec3 origin = originIn;
	PxVec3 translation = translationIn;
	World* world = world_of( id );
	float length = translation.magnitude();
	if ( world == nullptr || length <= 0.0f )
	{
		return 0;
	}
	std::vector<PxRaycastHit> touches( MaxTouches );
	PxRaycastBuffer buffer( touches.data(), MaxTouches );
	QueryFilter filter;
	filter.hitTriggers = hitTriggers != 0;
	world->scene->raycast( origin, translation / length, length, buffer, PxHitFlag::ePOSITION | PxHitFlag::eNORMAL | PxHitFlag::eFACE_INDEX,
						   query_filter( maskBits ), &filter );
	return collect( buffer.touches, buffer.nbTouches, length, closest, hits, capacity );
}

UA3_API int ua3_shapecast( uint32_t id, int kind, ua3_vec3 aIn, ua3_vec3 bIn, ua3_quat rotationIn, float radius, ua3_vec3 translationIn, uint64_t maskBits,
						   int hitTriggers, int closest, ua3_hit* hits, int capacity )
{
	PxVec3 a = aIn;
	PxVec3 b = bIn;
	PxQuat rotation = rotationIn;
	PxVec3 translation = translationIn;
	World* world = world_of( id );
	float length = translation.magnitude();
	if ( world == nullptr || length <= 0.0f )
	{
		return 0;
	}
	PxTransform pose;
	PxGeometryHolder geometry = query_geometry( kind, a, b, rotation, radius, pose );
	std::vector<PxSweepHit> touches( MaxTouches );
	PxSweepBuffer buffer( touches.data(), MaxTouches );
	QueryFilter filter;
	filter.hitTriggers = hitTriggers != 0;
	world->scene->sweep( geometry.any(), pose, translation / length, length, buffer,
						 PxHitFlag::ePOSITION | PxHitFlag::eNORMAL | PxHitFlag::eFACE_INDEX, query_filter( maskBits ), &filter );
	return collect( buffer.touches, buffer.nbTouches, length, closest, hits, capacity );
}

// The shapes overlapping a query shape (see query_geometry). Returns their number; at most
// capacity are written.
UA3_API int ua3_overlap( uint32_t id, int kind, ua3_vec3 aIn, ua3_vec3 bIn, ua3_quat rotationIn, float radius, uint64_t maskBits, int hitTriggers,
						 uint64_t* shapes, int capacity )
{
	PxVec3 a = aIn;
	PxVec3 b = bIn;
	PxQuat rotation = rotationIn;
	World* world = world_of( id );
	if ( world == nullptr )
	{
		return 0;
	}
	PxTransform pose;
	PxGeometryHolder geometry = query_geometry( kind, a, b, rotation, radius, pose );
	std::vector<PxOverlapHit> touches( MaxTouches );
	PxOverlapBuffer buffer( touches.data(), MaxTouches );
	QueryFilter filter;
	filter.hitTriggers = hitTriggers != 0;
	world->scene->overlap( geometry.any(), pose, buffer, query_filter( maskBits ), &filter );
	for ( PxU32 i = 0; i < buffer.nbTouches && static_cast<int>( i ) < capacity; ++i )
	{
		shapes[i] = pack( buffer.touches[i].shape );
	}
	return static_cast<int>( buffer.nbTouches );
}

// The shapes whose bounds overlap an axis-aligned box.
UA3_API int ua3_overlap_aabb( uint32_t id, ua3_vec3 lowerIn, ua3_vec3 upperIn, uint64_t maskBits, int hitTriggers, uint64_t* shapes, int capacity )
{
	PxVec3 lower = lowerIn;
	PxVec3 upper = upperIn;
	PxVec3 center = ( lower + upper ) * 0.5f, half = ( upper - lower ) * 0.5f;
	return ua3_overlap( id, 2, center, half, ua3_quat{ 0.0f, 0.0f, 0.0f, 1.0f }, 0.0f, maskBits, hitTriggers, shapes, capacity );
}

// Physics.ComputePenetration: two shapes, each with its actor frame placed at a pose. Returns 1
// when they overlap, with the direction to move the first out of the second and how far.
UA3_API int ua3_shapes_penetration( uint64_t a, ua3_vec3 positionAIn, ua3_quat rotationAIn, uint64_t b, ua3_vec3 positionBIn, ua3_quat rotationBIn,
									ua3_vec3* direction, float* depth )
{
	PxShape* sa = shape_of( a );
	PxShape* sb = shape_of( b );
	*direction = ua3_vec3( PxVec3( 0.0f ) );
	*depth = 0.0f;
	if ( sa == nullptr || sb == nullptr )
	{
		return 0;
	}
	PxQuat qa = rotationAIn;
	PxQuat qb = rotationBIn;
	PxTransform poseA = PxTransform( positionAIn, qa.getNormalized() ) * sa->getLocalPose();
	PxTransform poseB = PxTransform( positionBIn, qb.getNormalized() ) * sb->getLocalPose();
	PxVec3 dir;
	PxReal d;
	bool overlap = PxGeometryQuery::computePenetration( dir, d, sa->getGeometry().any(), poseA, sb->getGeometry().any(), poseB );
	if ( !overlap )
	{
		return 0;
	}
	*direction = ua3_vec3( dir );
	*depth = d;
	return 1;
}

// -------------------------------------------------------------------------------------- joints

// Joints as Unity builds them: the joint's own body is actor0 (its frame at the anchor, x along
// the axis), the connected body (or the world, 0) actor1. Kinds: 0 D6 (hinge, character,
// configurable, fixed), 1 distance (spring). Drive limits are forces.
UA3_API uint64_t ua3_joint_create( uint32_t id, int kind, uint64_t body0, ua3_vec3 position0In, ua3_quat rotation0In, uint64_t body1,
								   ua3_vec3 position1In, ua3_quat rotation1In )
{
	World* world = world_of( id );
	PxRigidActor* a0 = body_of( body0 );
	PxRigidActor* a1 = body1 != 0 ? body_of( body1 ) : nullptr;
	if ( world == nullptr || a0 == nullptr )
	{
		return 0;
	}
	PxQuat q0 = rotation0In;
	PxQuat q1 = rotation1In;
	PxTransform frame0( position0In, q0.getNormalized() ), frame1( position1In, q1.getNormalized() );
	PxJoint* joint = nullptr;
	if ( kind == 1 )
	{
		joint = PxDistanceJointCreate( *s_physics, a0, frame0, a1, frame1 );
	}
	else
	{
		joint = PxD6JointCreate( *s_physics, a0, frame0, a1, frame1 );
	}
	if ( joint == nullptr )
	{
		return 0;
	}
	joint->setConstraintFlag( PxConstraintFlag::eDRIVE_LIMITS_ARE_FORCES, true );
	s_joints.insert( joint );
	return pack( joint );
}

UA3_API void ua3_joint_destroy( uint64_t id )
{
	if ( PxJoint* joint = joint_of( id ) )
	{
		s_joints.erase( joint );
		joint->release();
	}
}

UA3_API void ua3_joint_set_frames( uint64_t id, ua3_vec3 position0In, ua3_quat rotation0In, ua3_vec3 position1In, ua3_quat rotation1In )
{
	if ( PxJoint* joint = joint_of( id ) )
	{
		PxQuat q0 = rotation0In;
		PxQuat q1 = rotation1In;
		joint->setLocalPose( PxJointActorIndex::eACTOR0, PxTransform( position0In, q0.getNormalized() ) );
		joint->setLocalPose( PxJointActorIndex::eACTOR1, PxTransform( position1In, q1.getNormalized() ) );
	}
}

// breakForce/breakTorque (PX_MAX for none), collision between the bodies, preprocessing, the
// mass scales (Unity's massScale and connectedMassScale scale both inverse mass and inertia).
UA3_API void ua3_joint_set_common( uint64_t id, float breakForce, float breakTorque, int collision, int preprocessing, float massScale0,
								   float massScale1 )
{
	PxJoint* joint = joint_of( id );
	if ( joint == nullptr )
	{
		return;
	}
	joint->setBreakForce( breakForce, breakTorque );
	joint->setConstraintFlag( PxConstraintFlag::eCOLLISION_ENABLED, collision != 0 );
	joint->setConstraintFlag( PxConstraintFlag::eDISABLE_PREPROCESSING, preprocessing == 0 );
	// Unity's mass scales scale the inverse mass and inertia (measured).
	float inv0 = PxMax( massScale0, 0.0f );
	float inv1 = PxMax( massScale1, 0.0f );
	joint->setInvMassScale0( inv0 );
	joint->setInvInertiaScale0( inv0 );
	joint->setInvMassScale1( inv1 );
	joint->setInvInertiaScale1( inv1 );
}

// The force and torque the joint last applied to its own body.
UA3_API void ua3_joint_force( uint64_t id, ua3_vec3* force, ua3_vec3* torque )
{
	PxJoint* joint = joint_of( id );
	PxVec3 f( 0.0f ), t( 0.0f );
	if ( joint != nullptr )
	{
		joint->getConstraint()->getForce( f, t );
	}
	*force = ua3_vec3( f );
	*torque = ua3_vec3( t );
}

UA3_API int ua3_joint_is_broken( uint64_t id )
{
	PxJoint* joint = joint_of( id );
	return joint != nullptr && ( joint->getConstraintFlags() & PxConstraintFlag::eBROKEN );
}

// The joints broken in the last step, with the force that broke them.
UA3_API int ua3_world_broken_joints( uint32_t id, uint64_t* joints, float* forces, int capacity )
{
	World* world = world_of( id );
	if ( world == nullptr )
	{
		return 0;
	}
	int count = static_cast<int>( world->broken.size() );
	for ( int i = 0; i < count && i < capacity; ++i )
	{
		joints[i] = world->broken[i].first;
		PxJoint* joint = joint_of( joints[i] );
		PxVec3 force( 0.0f ), torque( 0.0f );
		if ( joint != nullptr )
		{
			joint->getConstraint()->getForce( force, torque );
		}
		forces[i] = force.magnitude();
	}
	return count;
}

// D6: motion per axis (0 x, 1 y, 2 z, 3 twist, 4 swing1, 5 swing2; 0 locked, 1 limited, 2 free).
UA3_API void ua3_d6_set_motion( uint64_t id, int axis, int motion )
{
	if ( PxD6Joint* d6 = d6_of( id ) )
	{
		d6->setMotion( static_cast<PxD6Axis::Enum>( axis ), static_cast<PxD6Motion::Enum>( motion ) );
	}
}

// A limit's softness: stiffness > 0 makes it a spring. Linear limits take the contact distance as it
// is; angular ones turn 0 into 0.01 (both measured).
UA3_API void ua3_d6_set_linear_limit( uint64_t id, float extent, float stiffness, float damping, float restitution, float bounceThreshold,
									  float contactDistance )
{
	PxD6Joint* d6 = d6_of( id );
	if ( d6 == nullptr )
	{
		return;
	}
	PxJointLinearLimit limit = stiffness > 0.0f ? PxJointLinearLimit( extent, PxSpring( stiffness, damping ) )
												: PxJointLinearLimit( s_physics->getTolerancesScale(), extent, PxMax( contactDistance, 0.0f ) );
	limit.restitution = restitution;
	limit.bounceThreshold = bounceThreshold;
	d6->setDistanceLimit( limit );
}

UA3_API void ua3_d6_set_twist_limit( uint64_t id, float lower, float upper, float stiffness, float damping, float restitution, float bounceThreshold,
									 float contactDistance )
{
	PxD6Joint* d6 = d6_of( id );
	if ( d6 == nullptr )
	{
		return;
	}
	PxJointAngularLimitPair limit = stiffness > 0.0f ? PxJointAngularLimitPair( lower, upper, PxSpring( stiffness, damping ) )
													 : PxJointAngularLimitPair( lower, upper, contact_distance( contactDistance ) );
	limit.restitution = restitution;
	limit.bounceThreshold = bounceThreshold;
	d6->setTwistLimit( limit );
}

UA3_API void ua3_d6_set_swing_limit( uint64_t id, float yAngle, float zAngle, float stiffness, float damping, float restitution, float bounceThreshold,
									 float contactDistance )
{
	PxD6Joint* d6 = d6_of( id );
	if ( d6 == nullptr )
	{
		return;
	}
	PxJointLimitCone limit = stiffness > 0.0f ? PxJointLimitCone( yAngle, zAngle, PxSpring( stiffness, damping ) )
											  : PxJointLimitCone( yAngle, zAngle, contact_distance( contactDistance ) );
	limit.restitution = restitution;
	limit.bounceThreshold = bounceThreshold;
	d6->setSwingLimit( limit );
}

// Drives: 0 x, 1 y, 2 z, 3 swing, 4 twist, 5 slerp.
UA3_API void ua3_d6_set_drive( uint64_t id, int index, float stiffness, float damping, float forceLimit, int acceleration )
{
	if ( PxD6Joint* d6 = d6_of( id ) )
	{
		d6->setDrive( static_cast<PxD6Drive::Enum>( index ), PxD6JointDrive( stiffness, damping, forceLimit, acceleration != 0 ) );
	}
}

UA3_API void ua3_d6_set_drive_target( uint64_t id, ua3_vec3 positionIn, ua3_quat rotationIn, ua3_vec3 linearIn, ua3_vec3 angularIn )
{
	if ( PxD6Joint* d6 = d6_of( id ) )
	{
		PxQuat q = rotationIn;
		d6->setDrivePosition( PxTransform( positionIn, q.getNormalized() ) );
		d6->setDriveVelocity( linearIn, angularIn );
	}
}

// The twist angle and its speed (actor1 relative to actor0 about the frame's x axis).
UA3_API void ua3_d6_twist( uint64_t id, float* angle, float* velocity )
{
	*angle = 0.0f;
	*velocity = 0.0f;
	PxD6Joint* d6 = d6_of( id );
	if ( d6 == nullptr )
	{
		return;
	}
	*angle = d6->getTwistAngle();
	PxRigidActor* actors[2];
	d6->getActors( actors[0], actors[1] );
	PxVec3 w0( 0.0f ), w1( 0.0f );
	if ( actors[0] != nullptr && actors[0]->is<PxRigidBody>() )
	{
		w0 = actors[0]->is<PxRigidBody>()->getAngularVelocity();
	}
	if ( actors[1] != nullptr && actors[1]->is<PxRigidBody>() )
	{
		w1 = actors[1]->is<PxRigidBody>()->getAngularVelocity();
	}
	PxTransform frame0 = d6->getLocalPose( PxJointActorIndex::eACTOR0 );
	PxTransform pose0 = actors[0] != nullptr ? actors[0]->getGlobalPose() : PxTransform( PxIdentity );
	PxVec3 axis = ( pose0.q * frame0.q ).getBasisVector0();
	*velocity = ( w1 - w0 ).dot( axis );
}

UA3_API void ua3_distance_set( uint64_t id, float minDistance, float maxDistance, float tolerance, float stiffness, float damping )
{
	PxJoint* joint = joint_of( id );
	PxDistanceJoint* distance = joint != nullptr ? joint->is<PxDistanceJoint>() : nullptr;
	if ( distance == nullptr )
	{
		return;
	}
	distance->setMinDistance( minDistance );
	distance->setMaxDistance( maxDistance );
	distance->setTolerance( tolerance );
	distance->setStiffness( stiffness );
	distance->setDamping( damping );
	distance->setDistanceJointFlags( PxDistanceJointFlag::eMAX_DISTANCE_ENABLED | PxDistanceJointFlag::eMIN_DISTANCE_ENABLED |
									 PxDistanceJointFlag::eSPRING_ENABLED );
}


// ----------------------------------------------------------------------------- vehicle api

// A wheel as the vehicle uses it: positions relative to the body's center of mass in body axes.
struct ua3_wheel_def
{
	ua3_vec3 centre;     // the wheel's centre at rest (zero jounce)
	ua3_vec3 travel;     // the suspension's travel direction (down)
	ua3_vec3 forcePoint; // where suspension and tire forces apply
	float radius, mass, dampingRate;
	float spring, damper, maxCompression, maxDroop, sprungMass;
	float forward[5], sideways[5];
	uint32_t layerMask;
	int enabled;
};

struct ua3_wheel_state
{
	int grounded;
	float omega;  // the corrected rotation speed (rad/s), as rpm reports it
	float jounce;
	float steer;
	float rotationAngle;
	uint64_t shape;
	ua3_vec3 point, normal;
	float force;
	ua3_vec3 forwardDir, sidewaysDir;
	float forwardSlip, sidewaysSlip;
	float rawOmega; // the wheel's own rotation speed (rad/s)
};

// A PhysX vehicle (PxVehicleNoDrive) on a dynamic body with a number of wheels; 0 when it cannot.
UA3_API uint64_t ua3_vehicle_create( uint64_t body, int wheels )
{
	PxRigidDynamic* actor = dynamic_of( body );
	World* world = actor != nullptr ? world_of_actor( actor ) : nullptr;
	if ( world == nullptr || wheels <= 0 || wheels > PX_MAX_NB_WHEELS )
	{
		return 0;
	}
	Vehicle* v = new Vehicle();
	v->world = world;
	v->actor = actor;
	v->wheels = static_cast<PxU32>( wheels );
	v->curves.resize( v->wheels );
	PxVehicleWheelsSimData* data = PxVehicleWheelsSimData::allocate( v->wheels );
	for ( PxU32 i = 0; i < v->wheels; ++i )
	{
		data->setWheelShapeMapping( i, -1 );
		PxVehicleWheelData wheel;
		wheel.mRadius = 0.5f;
		wheel.mMass = 20.0f;
		wheel.mMOI = 2.5f;
		data->setWheelData( i, wheel );
		PxVehicleSuspensionData suspension;
		suspension.mMaxCompression = 0.15f;
		suspension.mMaxDroop = 0.15f;
		suspension.mSprungMass = 1.0f;
		data->setSuspensionData( i, suspension );
		data->setTireData( i, PxVehicleTireData() );
		data->setSuspTravelDirection( i, PxVec3( 0.0f, -1.0f, 0.0f ) );
	}
	data->setSubStepCount( 5.0f, 5, 1 );
	v->drive = PxVehicleNoDrive::allocate( v->wheels );
	v->drive->setup( s_physics, actor, *data );
	data->free();
	v->drive->mWheelsDynData.setTireForceShaderFunction( unity_tire );
	for ( PxU32 i = 0; i < v->wheels; ++i )
	{
		v->drive->mWheelsDynData.setTireForceShaderData( i, &v->curves[i] );
	}
	// No setToRestState: it would zero the body's velocity (the wheels start at rest anyway).
	world->vehicles.push_back( v );
	s_vehicles.insert( v );
	return pack( v );
}

UA3_API void ua3_vehicle_destroy( uint64_t id )
{
	if ( Vehicle* v = vehicle_of( id ) )
	{
		release_vehicle( v );
	}
}

// The wheels' set-up for the coming step, the substeps, and whether the suspensions limit their
// expansion speed.
UA3_API void ua3_vehicle_configure( uint64_t id, const ua3_wheel_def* wheels, int count, float threshold, int below, int above, int expansionLimited )
{
	Vehicle* v = vehicle_of( id );
	if ( v == nullptr || static_cast<PxU32>( count ) != v->wheels )
	{
		return;
	}
	PxVehicleWheelsSimData& data = v->drive->mWheelsSimData;
	uint64_t actor = pack( v->actor );
	for ( PxU32 i = 0; i < v->wheels; ++i )
	{
		const ua3_wheel_def& w = wheels[i];
		PxVehicleWheelData wheel;
		wheel.mRadius = w.radius;
		wheel.mMass = w.mass;
		wheel.mMOI = 0.5f * w.mass * w.radius * w.radius;
		wheel.mDampingRate = w.dampingRate;
		wheel.mMaxBrakeTorque = 0.0f;
		wheel.mMaxHandBrakeTorque = 0.0f;
		wheel.mMaxSteer = PxPi;
		data.setWheelData( i, wheel );
		PxVehicleSuspensionData suspension;
		suspension.mSpringStrength = w.spring;
		suspension.mSpringDamperRate = w.damper;
		suspension.mMaxCompression = w.maxCompression;
		suspension.mMaxDroop = w.maxDroop;
		suspension.mSprungMass = w.sprungMass;
		data.setSuspensionData( i, suspension );
		data.setSuspTravelDirection( i, w.travel );
		data.setWheelCentreOffset( i, w.centre );
		data.setSuspForceAppPointOffset( i, w.forcePoint );
		data.setTireForceAppPointOffset( i, w.forcePoint );
		data.setSceneQueryFilterData( i, PxFilterData( w.layerMask, static_cast<PxU32>( actor ), static_cast<PxU32>( actor >> 32 ), 0 ) );
		std::memcpy( v->curves[i].forward, w.forward, sizeof( w.forward ) );
		std::memcpy( v->curves[i].sideways, w.sideways, sizeof( w.sideways ) );
		if ( w.enabled )
		{
			if ( data.getIsWheelDisabled( i ) )
			{
				data.enableWheel( i );
			}
		}
		else if ( !data.getIsWheelDisabled( i ) )
		{
			v->drive->setDriveTorque( i, 0.0f );
			v->drive->mWheelsDynData.setWheelRotationSpeed( i, 0.0f );
			data.disableWheel( i );
		}
	}
	data.setSubStepCount( threshold, static_cast<PxU32>( PxMax( below, 1 ) ), static_cast<PxU32>( PxMax( above, 1 ) ) );
	PxVehicleWheelsSimFlags flags = data.getFlags();
	if ( expansionLimited )
	{
		flags |= PxVehicleWheelsSimFlag::eLIMIT_SUSPENSION_EXPANSION_VELOCITY;
	}
	else
	{
		flags &= ~PxVehicleWheelsSimFlags( PxVehicleWheelsSimFlag::eLIMIT_SUSPENSION_EXPANSION_VELOCITY );
	}
	data.setFlags( flags );
}

UA3_API void ua3_vehicle_inputs( uint64_t id, int index, float motorTorque, float brakeTorque, float steer )
{
	Vehicle* v = vehicle_of( id );
	if ( v == nullptr || index < 0 || static_cast<PxU32>( index ) >= v->wheels || v->drive->mWheelsSimData.getIsWheelDisabled( static_cast<PxU32>( index ) ) )
	{
		return;
	}
	v->drive->setDriveTorque( static_cast<PxU32>( index ), motorTorque );
	v->drive->setBrakeTorque( static_cast<PxU32>( index ), brakeTorque );
	v->drive->setSteerAngle( static_cast<PxU32>( index ), steer );
}

UA3_API void ua3_vehicle_set_wheel_speed( uint64_t id, int index, float omega )
{
	Vehicle* v = vehicle_of( id );
	if ( v != nullptr && index >= 0 && static_cast<PxU32>( index ) < v->wheels )
	{
		v->drive->mWheelsDynData.setWheelRotationSpeed( static_cast<PxU32>( index ), omega );
	}
}

// A wheel's state after the last step; 0 before the vehicle ever stepped.
UA3_API int ua3_vehicle_state( uint64_t id, int index, ua3_wheel_state* state )
{
	std::memset( state, 0, sizeof( *state ) );
	Vehicle* v = vehicle_of( id );
	if ( v == nullptr || index < 0 || static_cast<PxU32>( index ) >= v->wheels )
	{
		return 0;
	}
	PxU32 i = static_cast<PxU32>( index );
	state->omega = v->drive->mWheelsDynData.getWheelRotationSpeed( i );
	state->rawOmega = v->drive->mWheelsDynData.getWheel4DynData()[i >> 2].mWheelSpeeds[i & 3];
	state->rotationAngle = v->drive->mWheelsDynData.getWheelRotationAngle( i );
	if ( !v->simulated || i >= v->results.size() )
	{
		return 0;
	}
	const PxWheelQueryResult& r = v->results[i];
	state->grounded = r.isInAir ? 0 : 1;
	state->jounce = r.suspJounce;
	state->steer = r.steerAngle;
	state->shape = pack( r.tireContactShape );
	state->point = ua3_vec3( r.tireContactPoint );
	state->normal = ua3_vec3( r.tireContactNormal );
	state->force = r.suspSpringForce;
	state->forwardDir = ua3_vec3( r.tireLongitudinalDir );
	state->sidewaysDir = ua3_vec3( r.tireLateralDir );
	state->forwardSlip = r.longitudinalSlip;
	state->sidewaysSlip = r.lateralSlip;
	return 1;
}

// PhysX's sprung masses of wheels (positions relative to anything, the center of mass in the same
// frame), gravity along y.
UA3_API void ua3_sprung_masses( int count, const ua3_vec3* positions, ua3_vec3 centreIn, float mass, float* masses )
{
	if ( count <= 0 || count > PX_MAX_NB_WHEELS )
	{
		return;
	}
	std::vector<PxVec3> p( static_cast<size_t>( count ) );
	for ( int i = 0; i < count; ++i )
	{
		p[i] = positions[i];
	}
	PxVehicleComputeSprungMasses( static_cast<PxU32>( count ), p.data(), centreIn, mass, 1, masses );
}

// Frozen rotations (Unity's FreezeRotationX/Y/Z: lock bits 8, 16, 32) also make the inertia about
// those axes infinite, so that code integrating the body itself (vehicles) does not turn it
// (measured). The mass-space axes are taken as the body's.
UA3_API void ua3_body_freeze_inertia( uint64_t id, int locks )
{
	PxRigidDynamic* body = dynamic_of( id );
	if ( body == nullptr || ( locks & 56 ) == 0 )
	{
		return;
	}
	PxVec3 inertia = body->getMassSpaceInertiaTensor();
	if ( locks & 8 )
	{
		inertia.x = 0.0f;
	}
	if ( locks & 16 )
	{
		inertia.y = 0.0f;
	}
	if ( locks & 32 )
	{
		inertia.z = 0.0f;
	}
	body->setMassSpaceInertiaTensor( inertia );
}

// -------------------------------------------------------------------------- character controllers

struct ua3_cct_def
{
	ua3_vec3 position; // the capsule's centre
	float radius;
	float height; // between the caps' centres
	float contactOffset;
	float stepOffset;
	float slopeLimit; // the cosine of the steepest walkable slope
	int climbingMode; // PxCapsuleClimbingMode
	int nonWalkableMode; // PxControllerNonWalkableMode
	float density;
	float scaleCoeff;
	float volumeGrowth;
	uint64_t categoryBits;
	uint64_t maskBits;
	float friction;
	float restitution;
	uint64_t material;
	float staticFriction;
	int detectCollisions; // 0: bodies and other controllers pass through it
};

// A capsule controller in a world's controller manager; its kinematic actor's shape carries the
// layer's filter like a collider's shape (queries and triggers find it).
UA3_API uint64_t ua3_cct_create( uint32_t id, const ua3_cct_def* def )
{
	World* world = world_of( id );
	if ( world == nullptr )
	{
		return 0;
	}
	if ( world->controllers == nullptr )
	{
		world->controllers = PxCreateControllerManager( *world->scene );
		if ( world->controllers == nullptr )
		{
			return 0;
		}
	}
	Character* c = new Character();
	c->world = world;
	c->mask = static_cast<PxU32>( def->maskBits );
	PxCapsuleControllerDesc desc;
	PxVec3 position = def->position;
	desc.position = PxExtendedVec3( position.x, position.y, position.z );
	desc.radius = PxMax( def->radius, 1e-4f );
	desc.height = PxMax( def->height, 1e-4f );
	desc.contactOffset = PxMax( def->contactOffset, 1e-4f );
	desc.stepOffset = PxMax( def->stepOffset, 0.0f );
	desc.slopeLimit = PxMax( def->slopeLimit, 0.0f );
	desc.climbingMode = static_cast<PxCapsuleClimbingMode::Enum>( def->climbingMode );
	desc.nonWalkableMode = static_cast<PxControllerNonWalkableMode::Enum>( def->nonWalkableMode );
	desc.density = def->density;
	desc.scaleCoeff = def->scaleCoeff;
	desc.volumeGrowth = def->volumeGrowth;
	desc.upDirection = PxVec3( 0.0f, 1.0f, 0.0f );
	desc.material = material( def->staticFriction, def->friction, def->restitution, def->material );
	desc.reportCallback = c;
	desc.userData = c;
	PxController* controller = desc.isValid() ? world->controllers->createController( desc ) : nullptr;
	if ( controller == nullptr )
	{
		delete c;
		return 0;
	}
	c->controller = static_cast<PxCapsuleController*>( controller );
	PxRigidDynamic* actor = controller->getActor();
	actor->getShapes( &c->shape, 1 );
	ShapeInfo* i = new ShapeInfo{ 1.0f, true, true, 0 };
	c->shape->userData = i;
	c->shape->setSimulationFilterData( PxFilterData( static_cast<PxU32>( def->categoryBits ), def->detectCollisions ? c->mask : 0u, 0, 0 ) );
	set_filter_flags( c->shape );
	c->shape->setQueryFilterData( query_data( static_cast<PxU32>( def->categoryBits ), actor, false ) );
	refilter( c->shape );
	s_shapes.insert( c->shape );
	s_characters.insert( c );
	return pack( c );
}

UA3_API void ua3_cct_destroy( uint64_t id )
{
	Character* c = character_of( id );
	if ( c != nullptr )
	{
		release_character( c );
	}
}

UA3_API uint64_t ua3_cct_shape( uint64_t id )
{
	Character* c = character_of( id );
	return c != nullptr ? pack( c->shape ) : 0;
}

UA3_API void ua3_cct_set_filter( uint64_t id, uint64_t categoryBits, uint64_t maskBits, int detectCollisions )
{
	Character* c = character_of( id );
	if ( c == nullptr )
	{
		return;
	}
	c->mask = static_cast<PxU32>( maskBits );
	PxFilterData data = c->shape->getSimulationFilterData();
	data.word0 = static_cast<PxU32>( categoryBits );
	data.word1 = detectCollisions ? c->mask : 0u;
	c->shape->setSimulationFilterData( data );
	c->shape->setQueryFilterData( query_data( static_cast<PxU32>( categoryBits ), c->controller->getActor(), false ) );
	refilter( c->shape );
}

// The capsule's dimensions and walking settings (PhysX keeps the centre where it is).
UA3_API void ua3_cct_configure( uint64_t id, float radius, float height, float contactOffset, float stepOffset, float slopeLimit )
{
	Character* c = character_of( id );
	if ( c == nullptr )
	{
		return;
	}
	PxCapsuleController* cct = c->controller;
	if ( cct->getRadius() != PxMax( radius, 1e-4f ) )
	{
		cct->setRadius( PxMax( radius, 1e-4f ) );
	}
	if ( cct->getHeight() != PxMax( height, 1e-4f ) )
	{
		cct->setHeight( PxMax( height, 1e-4f ) );
	}
	cct->setContactOffset( PxMax( contactOffset, 1e-4f ) );
	cct->setStepOffset( PxMax( stepOffset, 0.0f ) );
	cct->setSlopeLimit( PxMax( slopeLimit, 0.0f ) );
}

UA3_API void ua3_cct_set_position( uint64_t id, ua3_vec3 positionIn )
{
	Character* c = character_of( id );
	if ( c != nullptr )
	{
		PxVec3 p = positionIn;
		c->controller->setPosition( PxExtendedVec3( p.x, p.y, p.z ) );
	}
}

UA3_API ua3_vec3 ua3_cct_position( uint64_t id )
{
	Character* c = character_of( id );
	if ( c == nullptr )
	{
		return PxVec3( 0.0f );
	}
	PxExtendedVec3 p = c->controller->getPosition();
	return PxVec3( static_cast<float>( p.x ), static_cast<float>( p.y ), static_cast<float>( p.z ) );
}

// Moves the controller; returns PhysX's collision flags (sides 1, up 2, down 4, as Unity's
// CollisionFlags) and keeps the touched shapes for ua3_cct_hits.
UA3_API int ua3_cct_move( uint64_t id, ua3_vec3 displacementIn, float minDistance, float elapsedTime, int overlapRecovery )
{
	Character* c = character_of( id );
	if ( c == nullptr )
	{
		return 0;
	}
	PxVec3 displacement = displacementIn;
	c->hits.clear();
	c->world->controllers->setOverlapRecoveryModule( overlapRecovery != 0 );
	PxFilterData data( c->mask, 0, 0, 0 );
	PxControllerFilters filters( &data, c, c );
	filters.mFilterFlags = PxQueryFlag::eSTATIC | PxQueryFlag::eDYNAMIC | PxQueryFlag::ePREFILTER;
	PxControllerCollisionFlags flags = c->controller->move( displacement, minDistance, elapsedTime, filters );
	return static_cast<int>( static_cast<PxU8>( flags ) );
}

UA3_API int ua3_cct_hits( uint64_t id, ua3_cct_hit* out, int capacity )
{
	Character* c = character_of( id );
	if ( c == nullptr )
	{
		return 0;
	}
	int count = static_cast<int>( c->hits.size() );
	for ( int i = 0; i < count && i < capacity; ++i )
	{
		out[i] = c->hits[i];
	}
	return count;
}
