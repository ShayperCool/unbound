// Unbound's flat C API over Box3D for the managed 3D physics (UnityEngine.Physics).
//
// Ids cross the boundary as integers: worlds as uint32_t, bodies and shapes as uint64_t (the bytes
// of b3WorldId/b3BodyId/b3ShapeId). Vectors and quaternions are b3Vec3 {x, y, z} and b3Quat
// {x, y, z, w}, the layout of Unity's Vector3 and Quaternion. Definitions are Unbound's own structs
// (ua3_*_def), so the managed side never mirrors Box3D's larger, still changing, def structs.
//
// Box3D's symbols stay hidden (-fvisibility=hidden); only the ua3_ functions are exported.

#include "box3d/box3d.h"
#include "box3d/collision.h"
#include "box3d/constants.h"
#include "box3d/math_functions.h"

#include <stdlib.h>
#include <string.h>

#if defined( _WIN32 )
	#define UA3_API __declspec( dllexport )
#else
	#define UA3_API __attribute__( ( visibility( "default" ) ) )
#endif

// The version of this API; the managed side checks it.
#define UA3_VERSION 1

// ------------------------------------------------------------------------------------------ ids

static uint32_t ua3_pack_world( b3WorldId id )
{
	uint32_t v;
	memcpy( &v, &id, sizeof( v ) );
	return v;
}

static b3WorldId ua3_world( uint32_t v )
{
	b3WorldId id;
	memcpy( &id, &v, sizeof( id ) );
	return id;
}

static uint64_t ua3_pack_body( b3BodyId id )
{
	uint64_t v;
	memcpy( &v, &id, sizeof( v ) );
	return v;
}

static b3BodyId ua3_body( uint64_t v )
{
	b3BodyId id;
	memcpy( &id, &v, sizeof( id ) );
	return id;
}

static uint64_t ua3_pack_shape( b3ShapeId id )
{
	uint64_t v;
	memcpy( &v, &id, sizeof( v ) );
	return v;
}

static b3ShapeId ua3_shape( uint64_t v )
{
	b3ShapeId id;
	memcpy( &id, &v, sizeof( id ) );
	return id;
}

_Static_assert( sizeof( b3WorldId ) == 4, "b3WorldId must be 4 bytes" );
_Static_assert( sizeof( b3BodyId ) == 8, "b3BodyId must be 8 bytes" );
_Static_assert( sizeof( b3ShapeId ) == 8, "b3ShapeId must be 8 bytes" );

UA3_API int ua3_version( void )
{
	return UA3_VERSION;
}

// ------------------------------------------------------------------------------------ materials

// A shape's user material id carries Unity's combine modes: bits 0-1 friction, bits 2-3 bounce
// (0 Average, 1 Minimum, 2 Multiply, 3 Maximum). Of two modes the larger wins, as in Unity.
static float ua3_combine( float a, float b, int mode )
{
	switch ( mode )
	{
		case 1:
			return a < b ? a : b;
		case 2:
			return a * b;
		case 3:
			return a > b ? a : b;
		default:
			return 0.5f * ( a + b );
	}
}

static float ua3_friction( float a, uint64_t materialA, float b, uint64_t materialB )
{
	int modeA = (int)( materialA & 3 ), modeB = (int)( materialB & 3 );
	return ua3_combine( a, b, modeA > modeB ? modeA : modeB );
}

static float ua3_restitution( float a, uint64_t materialA, float b, uint64_t materialB )
{
	int modeA = (int)( ( materialA >> 2 ) & 3 ), modeB = (int)( ( materialB >> 2 ) & 3 );
	return ua3_combine( a, b, modeA > modeB ? modeA : modeB );
}

// ------------------------------------------------------------------------------- ignored pairs

// Physics.IgnoreCollision pairs, checked by the custom filter that every shape enables.
typedef struct ua3_ignore
{
	uint32_t world;
	uint64_t a, b;
} ua3_ignore;

static ua3_ignore* s_ignores;
static int s_ignoreCount, s_ignoreCapacity;

static bool ua3_custom_filter( b3ShapeId shapeIdA, b3ShapeId shapeIdB, void* context )
{
	(void)context;
	if ( s_ignoreCount == 0 )
	{
		return true;
	}
	uint64_t a = ua3_pack_shape( shapeIdA ), b = ua3_pack_shape( shapeIdB );
	for ( int i = 0; i < s_ignoreCount; ++i )
	{
		if ( ( s_ignores[i].a == a && s_ignores[i].b == b ) || ( s_ignores[i].a == b && s_ignores[i].b == a ) )
		{
			return false;
		}
	}
	return true;
}

UA3_API void ua3_ignore_pair( uint32_t world, uint64_t a, uint64_t b, int ignore )
{
	for ( int i = 0; i < s_ignoreCount; ++i )
	{
		if ( ( s_ignores[i].a == a && s_ignores[i].b == b ) || ( s_ignores[i].a == b && s_ignores[i].b == a ) )
		{
			if ( !ignore )
			{
				s_ignores[i] = s_ignores[--s_ignoreCount];
			}
			return;
		}
	}
	if ( !ignore )
	{
		return;
	}
	if ( s_ignoreCount == s_ignoreCapacity )
	{
		int capacity = s_ignoreCapacity == 0 ? 16 : 2 * s_ignoreCapacity;
		ua3_ignore* grown = (ua3_ignore*)realloc( s_ignores, (size_t)capacity * sizeof( ua3_ignore ) );
		if ( grown == NULL )
		{
			return;
		}
		s_ignores = grown;
		s_ignoreCapacity = capacity;
	}
	s_ignores[s_ignoreCount++] = (ua3_ignore){ world, a, b };
}

// Forgets the pairs of a shape (destroyed) or of a whole world (shape 0).
static void ua3_forget_ignores( uint32_t world, uint64_t shape )
{
	for ( int i = 0; i < s_ignoreCount; )
	{
		bool match = s_ignores[i].world == world && ( shape == 0 || s_ignores[i].a == shape || s_ignores[i].b == shape );
		if ( match )
		{
			s_ignores[i] = s_ignores[--s_ignoreCount];
		}
		else
		{
			++i;
		}
	}
}

// ---------------------------------------------------------------------------------------- world

UA3_API uint32_t ua3_world_create( b3Vec3 gravity, float restitutionThreshold, float hitEventThreshold, int enableSleep )
{
	b3WorldDef def = b3DefaultWorldDef();
	def.gravity = gravity;
	def.restitutionThreshold = restitutionThreshold;
	def.hitEventThreshold = hitEventThreshold;
	def.enableSleep = enableSleep != 0;
	def.frictionCallback = ua3_friction;
	def.restitutionCallback = ua3_restitution;
	b3WorldId id = b3CreateWorld( &def );
	b3World_SetCustomFilterCallback( id, ua3_custom_filter, NULL );
	return ua3_pack_world( id );
}

UA3_API void ua3_world_destroy( uint32_t world )
{
	ua3_forget_ignores( world, 0 );
	if ( b3World_IsValid( ua3_world( world ) ) )
	{
		b3DestroyWorld( ua3_world( world ) );
	}
}

UA3_API void ua3_world_step( uint32_t world, float timeStep, int subStepCount )
{
	b3World_Step( ua3_world( world ), timeStep, subStepCount );
}

UA3_API void ua3_world_set_gravity( uint32_t world, b3Vec3 gravity )
{
	b3World_SetGravity( ua3_world( world ), gravity );
}

UA3_API void ua3_world_set_restitution_threshold( uint32_t world, float value )
{
	b3World_SetRestitutionThreshold( ua3_world( world ), value );
}

UA3_API void ua3_world_enable_sleeping( uint32_t world, int flag )
{
	b3World_EnableSleeping( ua3_world( world ), flag != 0 );
}

typedef struct ua3_pair
{
	uint64_t a, b;
} ua3_pair;

// The begin/end events of the last step: kind 0 contact begin, 1 contact end, 2 sensor begin
// (a = sensor), 3 sensor end. Returns the number of events; at most capacity are written.
UA3_API int ua3_world_events( uint32_t world, int kind, ua3_pair* out, int capacity )
{
	b3WorldId id = ua3_world( world );
	int count = 0;
	if ( kind <= 1 )
	{
		b3ContactEvents events = b3World_GetContactEvents( id );
		count = kind == 0 ? events.beginCount : events.endCount;
		for ( int i = 0; i < count && i < capacity; ++i )
		{
			b3ShapeId a = kind == 0 ? events.beginEvents[i].shapeIdA : events.endEvents[i].shapeIdA;
			b3ShapeId b = kind == 0 ? events.beginEvents[i].shapeIdB : events.endEvents[i].shapeIdB;
			out[i].a = ua3_pack_shape( a );
			out[i].b = ua3_pack_shape( b );
		}
	}
	else
	{
		b3SensorEvents events = b3World_GetSensorEvents( id );
		count = kind == 2 ? events.beginCount : events.endCount;
		for ( int i = 0; i < count && i < capacity; ++i )
		{
			b3ShapeId a = kind == 2 ? events.beginEvents[i].sensorShapeId : events.endEvents[i].sensorShapeId;
			b3ShapeId b = kind == 2 ? events.beginEvents[i].visitorShapeId : events.endEvents[i].visitorShapeId;
			out[i].a = ua3_pack_shape( a );
			out[i].b = ua3_pack_shape( b );
		}
	}
	return count;
}

// ----------------------------------------------------------------------------------------- body

// Motion lock bits: 1 linear x, 2 linear y, 4 linear z, 8 angular x, 16 angular y, 32 angular z.
typedef struct ua3_body_def
{
	int type; // 0 static, 1 kinematic, 2 dynamic
	b3Vec3 position;
	b3Quat rotation;
	b3Vec3 linearVelocity;
	b3Vec3 angularVelocity;
	float linearDamping;
	float angularDamping;
	float gravityScale;
	int locks;
	int enableSleep;
	int isAwake;
	int isBullet;
} ua3_body_def;

static b3MotionLocks ua3_locks( int bits )
{
	b3MotionLocks locks = { 0 };
	locks.linearX = ( bits & 1 ) != 0;
	locks.linearY = ( bits & 2 ) != 0;
	locks.linearZ = ( bits & 4 ) != 0;
	locks.angularX = ( bits & 8 ) != 0;
	locks.angularY = ( bits & 16 ) != 0;
	locks.angularZ = ( bits & 32 ) != 0;
	return locks;
}

UA3_API uint64_t ua3_body_create( uint32_t world, const ua3_body_def* def )
{
	b3BodyDef bodyDef = b3DefaultBodyDef();
	bodyDef.type = (b3BodyType)def->type;
	bodyDef.position = def->position;
	bodyDef.rotation = def->rotation;
	bodyDef.linearVelocity = def->linearVelocity;
	bodyDef.angularVelocity = def->angularVelocity;
	bodyDef.linearDamping = def->linearDamping;
	bodyDef.angularDamping = def->angularDamping;
	bodyDef.gravityScale = def->gravityScale;
	bodyDef.motionLocks = ua3_locks( def->locks );
	bodyDef.enableSleep = def->enableSleep != 0;
	bodyDef.isAwake = def->isAwake != 0;
	bodyDef.isBullet = def->isBullet != 0;
	return ua3_pack_body( b3CreateBody( ua3_world( world ), &bodyDef ) );
}

UA3_API void ua3_body_destroy( uint64_t body )
{
	b3BodyId id = ua3_body( body );
	if ( !b3Body_IsValid( id ) )
	{
		return;
	}
	b3ShapeId shapes[64];
	int count = b3Body_GetShapeCount( id );
	if ( count <= 64 )
	{
		b3Body_GetShapes( id, shapes, 64 );
		uint32_t world = ua3_pack_world( b3Body_GetWorld( id ) );
		for ( int i = 0; i < count; ++i )
		{
			ua3_forget_ignores( world, ua3_pack_shape( shapes[i] ) );
		}
	}
	b3DestroyBody( id );
}

UA3_API int ua3_body_is_valid( uint64_t body )
{
	return b3Body_IsValid( ua3_body( body ) );
}

UA3_API void ua3_body_set_type( uint64_t body, int type )
{
	b3Body_SetType( ua3_body( body ), (b3BodyType)type );
}

UA3_API void ua3_body_get_transform( uint64_t body, b3Vec3* position, b3Quat* rotation )
{
	b3WorldTransform t = b3Body_GetTransform( ua3_body( body ) );
	*position = t.p;
	*rotation = t.q;
}

UA3_API void ua3_body_set_transform( uint64_t body, b3Vec3 position, b3Quat rotation )
{
	b3Body_SetTransform( ua3_body( body ), position, rotation );
}

// Moves a kinematic body to a pose over the next step (Rigidbody.MovePosition/MoveRotation).
UA3_API void ua3_body_set_target_transform( uint64_t body, b3Vec3 position, b3Quat rotation, float timeStep )
{
	b3WorldTransform target = { position, rotation };
	b3Body_SetTargetTransform( ua3_body( body ), target, timeStep, true );
}

UA3_API b3Vec3 ua3_body_get_linear_velocity( uint64_t body )
{
	return b3Body_GetLinearVelocity( ua3_body( body ) );
}

UA3_API void ua3_body_set_linear_velocity( uint64_t body, b3Vec3 v )
{
	b3Body_SetLinearVelocity( ua3_body( body ), v );
}

UA3_API b3Vec3 ua3_body_get_angular_velocity( uint64_t body )
{
	return b3Body_GetAngularVelocity( ua3_body( body ) );
}

UA3_API void ua3_body_set_angular_velocity( uint64_t body, b3Vec3 v )
{
	b3Body_SetAngularVelocity( ua3_body( body ), v );
}

UA3_API b3Vec3 ua3_body_get_point_velocity( uint64_t body, b3Vec3 worldPoint )
{
	return b3Body_GetWorldPointVelocity( ua3_body( body ), worldPoint );
}

UA3_API b3Vec3 ua3_body_get_world_center( uint64_t body )
{
	return b3Body_GetWorldCenter( ua3_body( body ) );
}

// kind: 0 force at point, 1 force at center, 2 torque, 3 impulse at point, 4 impulse at center,
// 5 angular impulse.
UA3_API void ua3_body_apply( uint64_t body, int kind, b3Vec3 vector, b3Vec3 point )
{
	b3BodyId id = ua3_body( body );
	switch ( kind )
	{
		case 0:
			b3Body_ApplyForce( id, vector, point, true );
			break;
		case 1:
			b3Body_ApplyForceToCenter( id, vector, true );
			break;
		case 2:
			b3Body_ApplyTorque( id, vector, true );
			break;
		case 3:
			b3Body_ApplyLinearImpulse( id, vector, point, true );
			break;
		case 4:
			b3Body_ApplyLinearImpulseToCenter( id, vector, true );
			break;
		default:
			b3Body_ApplyAngularImpulse( id, vector, true );
			break;
	}
}

// The mass of the body's shapes at their densities: mass, local center of mass and the rotational
// inertia about that center (columns of a 3x3 matrix, in the body frame).
UA3_API void ua3_body_shape_mass( uint64_t body, float* mass, b3Vec3* center, b3Matrix3* inertia )
{
	b3BodyId id = ua3_body( body );
	b3Body_ApplyMassFromShapes( id );
	b3MassData data = b3Body_GetMassData( id );
	*mass = data.mass;
	*center = data.center;
	*inertia = data.inertia;
}

UA3_API void ua3_body_set_mass( uint64_t body, float mass, b3Vec3 center, const b3Matrix3* inertia )
{
	b3MassData data;
	data.mass = mass;
	data.center = center;
	data.inertia = *inertia;
	b3Body_SetMassData( ua3_body( body ), data );
}

UA3_API void ua3_body_set_damping( uint64_t body, float linear, float angular )
{
	b3BodyId id = ua3_body( body );
	b3Body_SetLinearDamping( id, linear );
	b3Body_SetAngularDamping( id, angular );
}

UA3_API void ua3_body_set_gravity_scale( uint64_t body, float scale )
{
	b3Body_SetGravityScale( ua3_body( body ), scale );
}

UA3_API int ua3_body_is_awake( uint64_t body )
{
	return b3Body_IsAwake( ua3_body( body ) );
}

UA3_API void ua3_body_set_awake( uint64_t body, int awake )
{
	b3Body_SetAwake( ua3_body( body ), awake != 0 );
}

UA3_API void ua3_body_enable_sleep( uint64_t body, int flag )
{
	b3Body_EnableSleep( ua3_body( body ), flag != 0 );
}

UA3_API void ua3_body_set_locks( uint64_t body, int locks )
{
	b3Body_SetMotionLocks( ua3_body( body ), ua3_locks( locks ) );
}

UA3_API void ua3_body_set_bullet( uint64_t body, int flag )
{
	b3Body_SetBullet( ua3_body( body ), flag != 0 );
}

UA3_API void ua3_body_set_enabled( uint64_t body, int enabled )
{
	b3BodyId id = ua3_body( body );
	if ( enabled )
	{
		b3Body_Enable( id );
	}
	else
	{
		b3Body_Disable( id );
	}
}

// ---------------------------------------------------------------------------------------- shape

typedef struct ua3_shape_def
{
	float density;
	float friction;
	float restitution;
	uint64_t material; // combine modes, see ua3_friction
	uint64_t categoryBits;
	uint64_t maskBits;
	int isSensor;
	int enableSensorEvents;
	int enableContactEvents;
	int updateBodyMass;
} ua3_shape_def;

static b3ShapeDef ua3_shape_def_of( const ua3_shape_def* def )
{
	b3ShapeDef shapeDef = b3DefaultShapeDef();
	shapeDef.density = def->density;
	shapeDef.baseMaterial.friction = def->friction;
	shapeDef.baseMaterial.restitution = def->restitution;
	shapeDef.baseMaterial.userMaterialId = def->material;
	shapeDef.filter.categoryBits = def->categoryBits;
	shapeDef.filter.maskBits = def->maskBits;
	shapeDef.isSensor = def->isSensor != 0;
	shapeDef.enableSensorEvents = def->enableSensorEvents != 0;
	shapeDef.enableContactEvents = def->enableContactEvents != 0;
	shapeDef.enableCustomFiltering = true;
	shapeDef.updateBodyMass = def->updateBodyMass != 0;
	return shapeDef;
}

UA3_API uint64_t ua3_shape_sphere( uint64_t body, const ua3_shape_def* def, b3Vec3 center, float radius )
{
	b3ShapeDef shapeDef = ua3_shape_def_of( def );
	b3Sphere sphere = { center, radius };
	return ua3_pack_shape( b3CreateSphereShape( ua3_body( body ), &shapeDef, &sphere ) );
}

UA3_API uint64_t ua3_shape_capsule( uint64_t body, const ua3_shape_def* def, b3Vec3 center1, b3Vec3 center2, float radius )
{
	b3ShapeDef shapeDef = ua3_shape_def_of( def );
	b3Capsule capsule = { center1, center2, radius };
	return ua3_pack_shape( b3CreateCapsuleShape( ua3_body( body ), &shapeDef, &capsule ) );
}

UA3_API uint64_t ua3_shape_box( uint64_t body, const ua3_shape_def* def, b3Vec3 center, b3Quat rotation, b3Vec3 halfExtents )
{
	b3ShapeDef shapeDef = ua3_shape_def_of( def );
	b3Transform transform = { center, rotation };
	b3BoxHull box = b3MakeTransformedBoxHull( halfExtents.x, halfExtents.y, halfExtents.z, transform );
	return ua3_pack_shape( b3CreateHullShape( ua3_body( body ), &shapeDef, &box.base ) );
}

// A convex hull of points in the body frame; 0 when they make no hull (degenerate input).
UA3_API uint64_t ua3_shape_hull( uint64_t body, const ua3_shape_def* def, const b3Vec3* points, int count )
{
	b3HullData* hull = b3CreateHull( points, count, B3_MAX_HULL_VERTICES );
	if ( hull == NULL )
	{
		return 0;
	}
	b3ShapeDef shapeDef = ua3_shape_def_of( def );
	b3ShapeId id = b3CreateHullShape( ua3_body( body ), &shapeDef, hull );
	b3DestroyHull( hull );
	return ua3_pack_shape( id );
}

// Triangle mesh data, owned by the caller until every shape using it is destroyed.
UA3_API void* ua3_mesh_create( const b3Vec3* vertices, int vertexCount, const int32_t* indices, int triangleCount )
{
	b3MeshDef def = { 0 };
	def.vertices = (b3Vec3*)vertices;
	def.stride = sizeof( b3Vec3 );
	def.indices = (int32_t*)indices;
	def.vertexCount = vertexCount;
	def.triangleCount = triangleCount;
	def.identifyEdges = true;
	// Unity's front faces wind clockwise.
	def.clockWiseWinding = true;
	return b3CreateMesh( &def, NULL, 0 );
}

UA3_API void ua3_mesh_destroy( void* mesh )
{
	if ( mesh != NULL )
	{
		b3DestroyMesh( (b3MeshData*)mesh );
	}
}

UA3_API uint64_t ua3_shape_mesh( uint64_t body, const ua3_shape_def* def, const void* mesh, b3Vec3 scale )
{
	b3ShapeDef shapeDef = ua3_shape_def_of( def );
	return ua3_pack_shape( b3CreateMeshShape( ua3_body( body ), &shapeDef, (const b3MeshData*)mesh, scale ) );
}

// Height field data (heights row by row, rows along z; a material per cell, 0xFF a hole), owned by
// the caller until every shape using it is destroyed.
UA3_API void* ua3_height_field_create( const float* heights, const uint8_t* materials, int countX, int countZ, b3Vec3 scale,
									   float minimumHeight, float maximumHeight )
{
	b3HeightFieldDef def = { 0 };
	def.heights = (float*)heights;
	def.materialIndices = (uint8_t*)materials;
	def.scale = scale;
	def.countX = countX;
	def.countZ = countZ;
	def.globalMinimumHeight = minimumHeight;
	def.globalMaximumHeight = maximumHeight;
	def.clockwiseWinding = false;
	return b3CreateHeightField( &def );
}

UA3_API void ua3_height_field_destroy( void* heightField )
{
	if ( heightField != NULL )
	{
		b3DestroyHeightField( (b3HeightFieldData*)heightField );
	}
}

UA3_API uint64_t ua3_shape_height_field( uint64_t body, const ua3_shape_def* def, const void* heightField )
{
	b3ShapeDef shapeDef = ua3_shape_def_of( def );
	return ua3_pack_shape( b3CreateHeightFieldShape( ua3_body( body ), &shapeDef, (const b3HeightFieldData*)heightField ) );
}

UA3_API void ua3_shape_destroy( uint64_t shape, int updateBodyMass )
{
	b3ShapeId id = ua3_shape( shape );
	if ( !b3Shape_IsValid( id ) )
	{
		return;
	}
	ua3_forget_ignores( ua3_pack_world( b3Shape_GetWorld( id ) ), shape );
	b3DestroyShape( id, updateBodyMass != 0 );
}

UA3_API int ua3_shape_is_valid( uint64_t shape )
{
	return b3Shape_IsValid( ua3_shape( shape ) );
}

UA3_API int ua3_shape_is_sensor( uint64_t shape )
{
	return b3Shape_IsSensor( ua3_shape( shape ) );
}

UA3_API uint64_t ua3_shape_body( uint64_t shape )
{
	return ua3_pack_body( b3Shape_GetBody( ua3_shape( shape ) ) );
}

UA3_API void ua3_shape_set_material( uint64_t shape, float friction, float restitution, uint64_t material )
{
	b3ShapeId id = ua3_shape( shape );
	b3SurfaceMaterial surface = b3Shape_GetSurfaceMaterial( id );
	surface.friction = friction;
	surface.restitution = restitution;
	surface.userMaterialId = material;
	b3Shape_SetSurfaceMaterial( id, surface );
}

UA3_API void ua3_shape_set_filter( uint64_t shape, uint64_t categoryBits, uint64_t maskBits )
{
	b3ShapeId id = ua3_shape( shape );
	b3Filter filter = b3Shape_GetFilter( id );
	filter.categoryBits = categoryBits;
	filter.maskBits = maskBits;
	b3Shape_SetFilter( id, filter, true );
}

UA3_API void ua3_shape_set_density( uint64_t shape, float density )
{
	b3Shape_SetDensity( ua3_shape( shape ), density, false );
}

// The shape's bounds, without the speculative margin Box3D keeps around them.
UA3_API void ua3_shape_aabb( uint64_t shape, b3AABB* bounds )
{
	b3AABB aabb = b3Shape_GetAABB( ua3_shape( shape ) );
	b3Vec3 margin = { B3_SPECULATIVE_DISTANCE, B3_SPECULATIVE_DISTANCE, B3_SPECULATIVE_DISTANCE };
	aabb.lowerBound = b3Add( aabb.lowerBound, margin );
	aabb.upperBound = b3Sub( aabb.upperBound, margin );
	*bounds = aabb;
}

UA3_API b3Vec3 ua3_shape_closest_point( uint64_t shape, b3Vec3 target )
{
	return b3Shape_GetClosestPoint( ua3_shape( shape ), target );
}

typedef struct ua3_hit
{
	uint64_t shape;
	b3Vec3 point;
	b3Vec3 normal;
	float fraction;
	int triangleIndex;
} ua3_hit;

UA3_API int ua3_shape_raycast( uint64_t shape, b3Vec3 origin, b3Vec3 translation, ua3_hit* hit )
{
	b3WorldCastOutput output = b3Shape_RayCast( ua3_shape( shape ), origin, translation );
	if ( !output.hit )
	{
		return 0;
	}
	hit->shape = shape;
	hit->point = output.point;
	hit->normal = output.normal;
	hit->fraction = output.fraction;
	hit->triangleIndex = output.triangleIndex;
	return 1;
}

typedef struct ua3_contact
{
	uint64_t shapeA, shapeB;
	b3Vec3 point;
	b3Vec3 normal; // from A to B
	float separation;
	float normalImpulse;
} ua3_contact;

// The touching contact points of a shape. Returns the number of points; at most capacity are written.
UA3_API int ua3_shape_contacts( uint64_t shape, ua3_contact* out, int capacity )
{
	b3ShapeId id = ua3_shape( shape );
	int contactCapacity = b3Shape_GetContactCapacity( id );
	if ( contactCapacity == 0 )
	{
		return 0;
	}
	b3ContactData* data = (b3ContactData*)malloc( (size_t)contactCapacity * sizeof( b3ContactData ) );
	if ( data == NULL )
	{
		return 0;
	}
	int contactCount = b3Shape_GetContactData( id, data, contactCapacity );
	int count = 0;
	for ( int i = 0; i < contactCount; ++i )
	{
		b3Vec3 centerA = b3Body_GetWorldCenter( b3Shape_GetBody( data[i].shapeIdA ) );
		for ( int m = 0; m < data[i].manifoldCount; ++m )
		{
			const b3Manifold* manifold = data[i].manifolds + m;
			for ( int p = 0; p < manifold->pointCount; ++p )
			{
				if ( count < capacity )
				{
					const b3ManifoldPoint* mp = manifold->points + p;
					ua3_contact* c = out + count;
					c->shapeA = ua3_pack_shape( data[i].shapeIdA );
					c->shapeB = ua3_pack_shape( data[i].shapeIdB );
					c->point = b3Add( centerA, mp->anchorA );
					c->normal = manifold->normal;
					c->separation = mp->separation;
					c->normalImpulse = mp->totalNormalImpulse;
				}
				++count;
			}
		}
	}
	free( data );
	return count;
}

// -------------------------------------------------------------------------------------- queries

typedef struct ua3_query
{
	ua3_hit* hits;
	int capacity;
	int count;
	int hitTriggers;
	int closest;
} ua3_query;

static b3QueryFilter ua3_query_filter( uint64_t maskBits )
{
	b3QueryFilter filter = b3DefaultQueryFilter();
	filter.categoryBits = ~0ull;
	filter.maskBits = maskBits;
	return filter;
}

static float ua3_cast_callback( b3ShapeId shapeId, b3Pos point, b3Vec3 normal, float fraction, uint64_t userMaterialId,
								int triangleIndex, int childIndex, void* context )
{
	(void)userMaterialId;
	(void)childIndex;
	ua3_query* query = (ua3_query*)context;
	if ( !query->hitTriggers && b3Shape_IsSensor( shapeId ) )
	{
		return -1.0f;
	}
	ua3_hit hit = { ua3_pack_shape( shapeId ), point, normal, fraction, triangleIndex };
	if ( query->closest )
	{
		// Unity's single casts miss the shapes they start in.
		if ( fraction <= 0.0f )
		{
			return -1.0f;
		}
		query->hits[0] = hit;
		query->count = 1;
		return fraction;
	}
	if ( query->count < query->capacity )
	{
		query->hits[query->count] = hit;
	}
	query->count += 1;
	return 1.0f;
}

// Ray casts: the closest hit (closest != 0, one hit written) or all hits along the ray. Returns
// the number of hits; at most capacity are written.
UA3_API int ua3_raycast( uint32_t world, b3Vec3 origin, b3Vec3 translation, uint64_t maskBits, int hitTriggers, int closest,
						 ua3_hit* hits, int capacity )
{
	ua3_query query = { hits, capacity, 0, hitTriggers, closest };
	b3World_CastRay( ua3_world( world ), origin, translation, ua3_query_filter( maskBits ), ua3_cast_callback, &query );
	return query.count;
}

// The query shapes: kind 0 sphere (a = center, radius), 1 capsule (a, b = centers, radius),
// 2 box (a = center, b = half extents, rotation).
static int ua3_proxy_points( int kind, b3Vec3 a, b3Vec3 b, b3Quat rotation, b3Vec3* points )
{
	if ( kind == 0 )
	{
		points[0] = b3Vec3_zero;
		return 1;
	}
	if ( kind == 1 )
	{
		points[0] = b3Vec3_zero;
		points[1] = b3Sub( b, a );
		return 2;
	}
	int n = 0;
	for ( int i = 0; i < 8; ++i )
	{
		b3Vec3 corner = { ( i & 1 ) ? b.x : -b.x, ( i & 2 ) ? b.y : -b.y, ( i & 4 ) ? b.z : -b.z };
		points[n++] = b3RotateVector( rotation, corner );
	}
	return n;
}

UA3_API int ua3_shapecast( uint32_t world, int kind, b3Vec3 a, b3Vec3 b, b3Quat rotation, float radius, b3Vec3 translation,
						   uint64_t maskBits, int hitTriggers, int closest, ua3_hit* hits, int capacity )
{
	b3Vec3 points[8];
	b3ShapeProxy proxy;
	proxy.points = points;
	proxy.count = ua3_proxy_points( kind, a, b, rotation, points );
	proxy.radius = kind == 2 ? 0.0f : radius;
	ua3_query query = { hits, capacity, 0, hitTriggers, closest };
	b3World_CastShape( ua3_world( world ), a, &proxy, translation, ua3_query_filter( maskBits ), ua3_cast_callback, &query );
	return query.count;
}

typedef struct ua3_overlap_query
{
	uint64_t* shapes;
	int capacity;
	int count;
	int hitTriggers;
} ua3_overlap_query;

static bool ua3_overlap_callback( b3ShapeId shapeId, void* context )
{
	ua3_overlap_query* query = (ua3_overlap_query*)context;
	if ( !query->hitTriggers && b3Shape_IsSensor( shapeId ) )
	{
		return true;
	}
	if ( query->count < query->capacity )
	{
		query->shapes[query->count] = ua3_pack_shape( shapeId );
	}
	query->count += 1;
	return true;
}

// The shapes overlapping a query shape (see ua3_proxy_points). Returns their number; at most
// capacity are written.
UA3_API int ua3_overlap( uint32_t world, int kind, b3Vec3 a, b3Vec3 b, b3Quat rotation, float radius, uint64_t maskBits,
						 int hitTriggers, uint64_t* shapes, int capacity )
{
	b3Vec3 points[8];
	b3ShapeProxy proxy;
	proxy.points = points;
	proxy.count = ua3_proxy_points( kind, a, b, rotation, points );
	proxy.radius = kind == 2 ? 0.0f : radius;
	ua3_overlap_query query = { shapes, capacity, 0, hitTriggers };
	b3World_OverlapShape( ua3_world( world ), a, &proxy, ua3_query_filter( maskBits ), ua3_overlap_callback, &query );
	return query.count;
}

// The shapes whose bounds overlap an axis-aligned box.
UA3_API int ua3_overlap_aabb( uint32_t world, b3Vec3 lower, b3Vec3 upper, uint64_t maskBits, int hitTriggers, uint64_t* shapes,
							  int capacity )
{
	b3AABB bounds = { lower, upper };
	ua3_overlap_query query = { shapes, capacity, 0, hitTriggers };
	b3World_OverlapAABB( ua3_world( world ), bounds, ua3_query_filter( maskBits ), ua3_overlap_callback, &query );
	return query.count;
}
