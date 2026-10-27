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

using namespace physx;

#if defined( _WIN32 )
	#define UA3_API extern "C" __declspec( dllexport )
#else
	#define UA3_API extern "C" __attribute__( ( visibility( "default" ) ) )
#endif

// The version of this API; the managed side checks it.
#define UA3_VERSION 2

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
	void onConstraintBreak( PxConstraintInfo*, PxU32 ) override {}
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
	shape->setQueryFilterData( PxFilterData( static_cast<PxU32>( def->categoryBits ), 0, 0, 0 ) );
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
	shape->setQueryFilterData( PxFilterData( static_cast<PxU32>( categoryBits ), 0, 0, 0 ) );
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
