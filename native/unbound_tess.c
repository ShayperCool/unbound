// Polygon triangulation for UI Toolkit's Painter2D fills (managed/UnityEngine/UIElements/
// Rendering/UaVectorFill.cs): libtess2 (native/libtess2) with constrained Delaunay refinement,
// triangles, the non-zero or odd-even winding rule, as Unity's Painter2D fills triangulate their
// contours. Linked into the unbound_physx library next to the managed engine; only the ua_tess_
// functions are exported, libtess2's own functions stay hidden.
#include "tesselator.h"

#if defined(_WIN32)
#define UA_TESS_API __declspec(dllexport)
#else
#define UA_TESS_API __attribute__((visibility("default")))
#endif

// Triangulates `contours` closed contours of 2D points (`points`, `counts[i]` points each,
// concatenated). On success returns 0 and writes the output vertices (x, y pairs), each output
// vertex's input point index (-1 for a vertex libtess2 created at an intersection) and the
// triangles (three output vertex indices each). When a capacity is too small it returns 1 and only
// writes the counts, so the caller can grow its buffers and call again. Returns -1 when libtess2
// fails (out of memory, invalid input).
UA_TESS_API int ua_tess_triangulate(const float* points, const int* counts, int contours, int oddEven,
                                    float* outVertices, int* outVertexIndices, int vertexCapacity,
                                    int* outTriangles, int triangleCapacity, int* outVertexCount,
                                    int* outTriangleCount)
{
	TESStesselator* tess = tessNewTess(0);
	if (!tess)
		return -1;
	const float* p = points;
	for (int i = 0; i < contours; i++)
	{
		tessAddContour(tess, 2, p, sizeof(float) * 2, counts[i]);
		p += counts[i] * 2;
	}
	tessSetOption(tess, TESS_CONSTRAINED_DELAUNAY_TRIANGULATION, 1);
	if (!tessTesselate(tess, oddEven ? TESS_WINDING_ODD : TESS_WINDING_NONZERO, TESS_POLYGONS, 3, 2, 0))
	{
		tessDeleteTess(tess);
		return -1;
	}
	int vertexCount = tessGetVertexCount(tess);
	int triangleCount = tessGetElementCount(tess);
	*outVertexCount = vertexCount;
	*outTriangleCount = triangleCount;
	if (vertexCount > vertexCapacity || triangleCount > triangleCapacity)
	{
		tessDeleteTess(tess);
		return 1;
	}
	const TESSreal* vertices = tessGetVertices(tess);
	const TESSindex* indices = tessGetVertexIndices(tess);
	const TESSindex* elements = tessGetElements(tess);
	for (int i = 0; i < vertexCount * 2; i++)
		outVertices[i] = vertices[i];
	for (int i = 0; i < vertexCount; i++)
		outVertexIndices[i] = indices[i] == TESS_UNDEF ? -1 : (int)indices[i];
	for (int i = 0; i < triangleCount * 3; i++)
		outTriangles[i] = elements[i];
	tessDeleteTess(tess);
	return 0;
}
