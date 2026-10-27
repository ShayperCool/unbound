// Unbound's flat C API over NvCloth 1.1 (the cloth solver the Unity 6000.0 Editor runs) for the
// managed Cloth component (UnityEngine.Cloth).
//
// Cloths are uint64_t ids (their Cloth*), all in one CPU solver stepped once per frame. Particles
// are {x, y, z, inverse mass} in the cloth's own frame, which follows its transform
// (uac_cloth_set_frame); collision spheres are in that frame too. Built into the unbound_physx
// library next to the PhysX shim, on the same PxShared; only the uac_ functions are exported.

#include "NvCloth/Callbacks.h"
#include "NvCloth/Cloth.h"
#include "NvCloth/Fabric.h"
#include "NvCloth/Factory.h"
#include "NvCloth/PhaseConfig.h"
#include "NvCloth/Solver.h"
#include "NvClothExt/ClothFabricCooker.h"
#include "foundation/PxAllocatorCallback.h"
#include "foundation/PxErrorCallback.h"
#include "foundation/PxQuat.h"
#include "foundation/PxVec4.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <unordered_set>
#include <vector>

#if defined( _WIN32 )
	#include <malloc.h>
	#define UAC_API extern "C" __declspec( dllexport )
#else
	#define UAC_API extern "C" __attribute__( ( visibility( "default" ) ) )
#endif

using namespace physx;

// Vectors cross the API as plain structs (PxVec3/PxVec4/PxQuat have copy constructors, which the
// C ABI passes by reference).
struct uac_vec3
{
	float x, y, z;
	operator PxVec3() const
	{
		return PxVec3( x, y, z );
	}
};

struct uac_quat
{
	float x, y, z, w;
	operator PxQuat() const
	{
		return PxQuat( x, y, z, w );
	}
};

namespace
{
class Allocator : public PxAllocatorCallback
{
public:
	void* allocate( size_t size, const char*, const char*, int ) override
	{
#if defined( _WIN32 )
		return _aligned_malloc( size, 16 );
#else
		void* p = nullptr;
		return posix_memalign( &p, 16, size ) == 0 ? p : nullptr;
#endif
	}
	void deallocate( void* ptr ) override
	{
#if defined( _WIN32 )
		_aligned_free( ptr );
#else
		free( ptr );
#endif
	}
};

class Errors : public PxErrorCallback
{
public:
	void reportError( PxErrorCode::Enum code, const char* message, const char* file, int line ) override
	{
		if ( code == PxErrorCode::eDEBUG_INFO || code == PxErrorCode::ePERF_WARNING )
		{
			return;
		}
		std::fprintf( stderr, "NvCloth: %s (%s:%d)\n", message, file, line );
	}
};

class Asserts : public nv::cloth::PxAssertHandler
{
public:
	void operator()( const char* exp, const char* file, int line, bool& ignore ) override
	{
		std::fprintf( stderr, "NvCloth: assertion %s (%s:%d)\n", exp, file, line );
		ignore = true;
	}
};

Allocator s_allocator;
Errors s_errors;
Asserts s_asserts;
nv::cloth::Factory* s_factory;
nv::cloth::Solver* s_solver;

bool library()
{
	if ( s_solver != nullptr )
	{
		return true;
	}
	nv::cloth::InitializeNvCloth( &s_allocator, &s_errors, &s_asserts, nullptr );
	s_factory = NvClothCreateFactoryCPU();
	if ( s_factory == nullptr )
	{
		return false;
	}
	s_solver = s_factory->createSolver();
	return s_solver != nullptr;
}

// A cloth with its fabric and the phase types its configuration goes by.
struct Cloth
{
	nv::cloth::Fabric* fabric = nullptr;
	nv::cloth::Cloth* cloth = nullptr;
	nv::cloth::Vector<int32_t>::Type phaseTypes;
};

std::unordered_set<Cloth*> s_cloths;

Cloth* cloth_of( uint64_t id )
{
	Cloth* c = reinterpret_cast<Cloth*>( static_cast<uintptr_t>( id ) );
	return s_cloths.count( c ) ? c : nullptr;
}
} // namespace

// The settings of a cloth, as one struct.
struct uac_params
{
	uac_vec3 gravity; // world space acceleration
	uac_vec3 damping;
	uac_vec3 linearDrag;
	uac_vec3 angularDrag;
	uac_vec3 linearInertia;
	uac_vec3 angularInertia;
	uac_vec3 centrifugalInertia;
	float solverFrequency;
	float stiffnessFrequency;
	float stretchStiffness; // vertical and horizontal phases
	float shearStiffness;
	float bendStiffness;
	float tetherStiffness;
	float tetherScale;
	float friction;
	float collisionMassScale;
	int continuousCollision;
	float selfCollisionDistance;
	float selfCollisionStiffness;
	float sleepThreshold;
	float motionConstraintStiffness;
};

UAC_API int uac_version()
{
	return 1;
}

// A cloth of particles {x, y, z, inverse mass} and triangles, its fabric cooked with gravity along
// a direction (which phases are vertical) and tethers geodesic or straight.
UAC_API uint64_t uac_cloth_create( const float* particles, int count, const uint32_t* triangles, int triangleCount,
								   uac_vec3 gravityIn, int geodesicTethers )
{
	PxVec3 gravity = gravityIn;
	if ( !library() || count <= 0 )
	{
		return 0;
	}
	std::vector<PxVec3> points( count );
	std::vector<float> invMasses( count );
	std::vector<PxVec4> start( count );
	for ( int i = 0; i < count; ++i )
	{
		points[i] = PxVec3( particles[4 * i], particles[4 * i + 1], particles[4 * i + 2] );
		invMasses[i] = particles[4 * i + 3];
		start[i] = PxVec4( points[i], invMasses[i] );
	}
	nv::cloth::ClothMeshDesc desc;
	desc.setToDefault();
	desc.points.data = points.data();
	desc.points.stride = sizeof( PxVec3 );
	desc.points.count = static_cast<PxU32>( count );
	desc.invMasses.data = invMasses.data();
	desc.invMasses.stride = sizeof( float );
	desc.invMasses.count = static_cast<PxU32>( count );
	desc.triangles.data = triangles;
	desc.triangles.stride = 3 * sizeof( uint32_t );
	desc.triangles.count = static_cast<PxU32>( triangleCount );
	Cloth* c = new Cloth();
	c->fabric = NvClothCookFabricFromMesh( s_factory, desc, gravity, &c->phaseTypes, geodesicTethers != 0 );
	if ( c->fabric == nullptr )
	{
		delete c;
		return 0;
	}
	c->cloth = s_factory->createCloth( nv::cloth::Range<const PxVec4>( start.data(), start.data() + count ), *c->fabric );
	if ( c->cloth == nullptr )
	{
		c->fabric->decRefCount();
		delete c;
		return 0;
	}
	s_solver->addCloth( c->cloth );
	s_cloths.insert( c );
	return static_cast<uint64_t>( reinterpret_cast<uintptr_t>( c ) );
}

UAC_API void uac_cloth_destroy( uint64_t id )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return;
	}
	s_solver->removeCloth( c->cloth );
	NV_CLOTH_DELETE( c->cloth );
	c->fabric->decRefCount();
	s_cloths.erase( c );
	delete c;
}

UAC_API void uac_cloth_configure( uint64_t id, const uac_params* p )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return;
	}
	nv::cloth::Cloth* cloth = c->cloth;
	cloth->setGravity( p->gravity );
	cloth->setDamping( p->damping );
	cloth->setLinearDrag( p->linearDrag );
	cloth->setAngularDrag( p->angularDrag );
	cloth->setLinearInertia( p->linearInertia );
	cloth->setAngularInertia( p->angularInertia );
	cloth->setCentrifugalInertia( p->centrifugalInertia );
	cloth->setSolverFrequency( p->solverFrequency );
	cloth->setStiffnessFrequency( p->stiffnessFrequency );
	std::vector<nv::cloth::PhaseConfig> configs( c->phaseTypes.size() );
	for ( size_t i = 0; i < configs.size(); ++i )
	{
		configs[i] = nv::cloth::PhaseConfig( static_cast<uint16_t>( i ) );
		switch ( c->phaseTypes[i] )
		{
			case nv::cloth::ClothFabricPhaseType::eBENDING:
				configs[i].mStiffness = p->bendStiffness;
				break;
			case nv::cloth::ClothFabricPhaseType::eSHEARING:
				configs[i].mStiffness = p->shearStiffness;
				break;
			default:
				configs[i].mStiffness = p->stretchStiffness;
				break;
		}
	}
	cloth->setPhaseConfig( nv::cloth::Range<const nv::cloth::PhaseConfig>( configs.data(), configs.data() + configs.size() ) );
	cloth->setTetherConstraintStiffness( p->tetherStiffness );
	cloth->setTetherConstraintScale( p->tetherScale );
	cloth->setFriction( p->friction );
	cloth->setCollisionMassScale( p->collisionMassScale );
	cloth->enableContinuousCollision( p->continuousCollision != 0 );
	cloth->setSelfCollisionDistance( p->selfCollisionDistance );
	cloth->setSelfCollisionStiffness( p->selfCollisionStiffness );
	cloth->setSleepThreshold( p->sleepThreshold );
	cloth->setMotionConstraintStiffness( p->motionConstraintStiffness );
}

UAC_API int uac_cloth_phase_types( uint64_t id, int32_t* out, int capacity )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return 0;
	}
	int count = static_cast<int>( c->phaseTypes.size() );
	for ( int i = 0; i < count && i < capacity; ++i )
	{
		out[i] = c->phaseTypes[i];
	}
	return count;
}

// The cloth's frame for this frame's step (its particles move with it as inertia says).
UAC_API void uac_cloth_set_frame( uint64_t id, uac_vec3 positionIn, uac_quat rotationIn )
{
	Cloth* c = cloth_of( id );
	if ( c != nullptr )
	{
		c->cloth->setTranslation( positionIn );
		c->cloth->setRotation( rotationIn );
	}
}

// Moves the frame and the particles with it, without inertia.
UAC_API void uac_cloth_teleport( uint64_t id, uac_vec3 positionIn, uac_quat rotationIn )
{
	Cloth* c = cloth_of( id );
	if ( c != nullptr )
	{
		c->cloth->teleportToLocation( positionIn, rotationIn );
	}
}

UAC_API void uac_cloth_clear_inertia( uint64_t id )
{
	Cloth* c = cloth_of( id );
	if ( c != nullptr )
	{
		c->cloth->clearInertia();
	}
}

namespace
{
void copy_constraints( nv::cloth::Range<PxVec4> range, const float* values, int count )
{
	for ( int i = 0; i < count && i < static_cast<int>( range.size() ); ++i )
	{
		range[i] = PxVec4( values[4 * i], values[4 * i + 1], values[4 * i + 2], values[4 * i + 3] );
	}
}
} // namespace

// Per particle {centre, radius} it must stay within (count 0 clears them).
UAC_API void uac_cloth_motion_constraints( uint64_t id, const float* values, int count )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return;
	}
	if ( count == 0 )
	{
		c->cloth->clearMotionConstraints();
		return;
	}
	copy_constraints( c->cloth->getMotionConstraints(), values, count );
}

// Per particle {centre, radius} it must stay out of (count 0 clears them).
UAC_API void uac_cloth_separation_constraints( uint64_t id, const float* values, int count )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return;
	}
	if ( count == 0 )
	{
		c->cloth->clearSeparationConstraints();
		return;
	}
	copy_constraints( c->cloth->getSeparationConstraints(), values, count );
}

// Collision spheres {centre, radius} in the cloth's frame, and capsules as pairs of sphere indices.
UAC_API void uac_cloth_collision( uint64_t id, const float* spheres, int sphereCount, const uint32_t* capsules, int capsuleCount )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return;
	}
	std::vector<PxVec4> s( sphereCount );
	for ( int i = 0; i < sphereCount; ++i )
	{
		s[i] = PxVec4( spheres[4 * i], spheres[4 * i + 1], spheres[4 * i + 2], spheres[4 * i + 3] );
	}
	c->cloth->setSpheres( nv::cloth::Range<const PxVec4>( s.data(), s.data() + s.size() ), 0, c->cloth->getNumSpheres() );
	c->cloth->setCapsules( nv::cloth::Range<const uint32_t>( capsules, capsules + 2 * capsuleCount ), 0, c->cloth->getNumCapsules() );
}

// Virtual particles: per particle 3 cloth particles and a weight index, and the weights.
UAC_API void uac_cloth_virtual_particles( uint64_t id, const uint32_t* indices, int count, const float* weights, int weightCount )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return;
	}
	typedef const uint32_t Index4[4];
	const Index4* begin = reinterpret_cast<const Index4*>( indices );
	std::vector<PxVec3> w( weightCount );
	for ( int i = 0; i < weightCount; ++i )
	{
		w[i] = PxVec3( weights[3 * i], weights[3 * i + 1], weights[3 * i + 2] );
	}
	c->cloth->setVirtualParticles( nv::cloth::Range<const uint32_t[4]>( begin, begin + count ),
								   nv::cloth::Range<const PxVec3>( w.data(), w.data() + w.size() ) );
}

UAC_API void uac_cloth_self_collision_indices( uint64_t id, const uint32_t* indices, int count )
{
	Cloth* c = cloth_of( id );
	if ( c != nullptr )
	{
		c->cloth->setSelfCollisionIndices( nv::cloth::Range<const uint32_t>( indices, indices + count ) );
	}
}

// Overwrites the particles {x, y, z, inverse mass} (current, and previous when asked).
UAC_API void uac_cloth_set_particles( uint64_t id, const float* values, int count, int previousToo )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return;
	}
	nv::cloth::MappedRange<PxVec4> current = c->cloth->getCurrentParticles();
	copy_constraints( nv::cloth::Range<PxVec4>( current.begin(), current.end() ), values, count );
	if ( previousToo )
	{
		nv::cloth::MappedRange<PxVec4> previous = c->cloth->getPreviousParticles();
		copy_constraints( nv::cloth::Range<PxVec4>( previous.begin(), previous.end() ), values, count );
	}
}

UAC_API int uac_cloth_particles( uint64_t id, float* out, int capacity )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return 0;
	}
	nv::cloth::MappedRange<const PxVec4> particles = nv::cloth::readCurrentParticles( *c->cloth );
	int count = static_cast<int>( particles.size() );
	for ( int i = 0; i < count && i < capacity; ++i )
	{
		out[4 * i] = particles[i].x;
		out[4 * i + 1] = particles[i].y;
		out[4 * i + 2] = particles[i].z;
		out[4 * i + 3] = particles[i].w;
	}
	return count;
}

UAC_API int uac_cloth_is_asleep( uint64_t id )
{
	Cloth* c = cloth_of( id );
	return c != nullptr && c->cloth->isAsleep() ? 1 : 0;
}

UAC_API void uac_cloth_wake( uint64_t id )
{
	Cloth* c = cloth_of( id );
	if ( c != nullptr )
	{
		c->cloth->wakeUp();
	}
}

// Leaves a cloth out of the solver's steps (disabled) or puts it back.
UAC_API void uac_cloth_set_simulated( uint64_t id, int simulated )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return;
	}
	s_solver->removeCloth( c->cloth );
	if ( simulated )
	{
		s_solver->addCloth( c->cloth );
	}
}

// Steps every simulated cloth by a frame's time.
UAC_API void uac_simulate( float dt )
{
	if ( s_solver == nullptr || dt <= 0.0f )
	{
		return;
	}
	if ( s_solver->beginSimulation( dt ) )
	{
		for ( int i = 0; i < s_solver->getSimulationChunkCount(); ++i )
		{
			s_solver->simulateChunk( i );
		}
	}
	s_solver->endSimulation();
}

UAC_API void uac_inter_collision( float distance, float stiffness, int iterations )
{
	if ( library() )
	{
		s_solver->setInterCollisionDistance( distance );
		s_solver->setInterCollisionStiffness( stiffness );
		s_solver->setInterCollisionNbIterations( static_cast<uint32_t>( iterations ) );
	}
}

// Collision spheres moving over the step from {centre, radius} start to target values, in the
// cloth's frame, and capsules as pairs of sphere indices.
UAC_API void uac_cloth_collision_motion( uint64_t id, const float* start, const float* target, int sphereCount,
										 const uint32_t* capsules, int capsuleCount )
{
	Cloth* c = cloth_of( id );
	if ( c == nullptr )
	{
		return;
	}
	std::vector<PxVec4> s( sphereCount ), t( sphereCount );
	for ( int i = 0; i < sphereCount; ++i )
	{
		s[i] = PxVec4( start[4 * i], start[4 * i + 1], start[4 * i + 2], start[4 * i + 3] );
		t[i] = PxVec4( target[4 * i], target[4 * i + 1], target[4 * i + 2], target[4 * i + 3] );
	}
	c->cloth->setSpheres( nv::cloth::Range<const PxVec4>( s.data(), s.data() + s.size() ),
						  nv::cloth::Range<const PxVec4>( t.data(), t.data() + t.size() ) );
	c->cloth->setCapsules( nv::cloth::Range<const uint32_t>( capsules, capsules + 2 * capsuleCount ), 0, c->cloth->getNumCapsules() );
}
