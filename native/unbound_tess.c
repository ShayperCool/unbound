// Polygon partitioning shared by 2D collider geometry and UI Toolkit Painter2D fills.
// libtess2's functions stay hidden; only the ua_tess_ entry points are exported.
#include "tesselator.h"

#if defined(_WIN32)
#define UA_TESS_API __declspec(dllexport)
#else
#define UA_TESS_API __attribute__((visibility("default")))
#endif

// Partitions `contours` closed contours of 2D points (`points`, `counts[i]` points each,
// concatenated). On success returns 0 and writes the output vertices (x, y pairs), each output
// vertex's input point index (-1 for a vertex libtess2 created at an intersection) and the
// polygons (polygonSize indices each, padded with -1). When a capacity is too small it returns 1 and only
// writes the counts, so the caller can grow its buffers and call again. Returns -1 when libtess2
// fails (out of memory, invalid input).
UA_TESS_API int ua_tess_polygons(const float* points, const int* counts, int contours, int oddEven,
                                 int delaunay, int polygonSize, float* outVertices, int* outVertexIndices,
                                 int vertexCapacity, int* outElements, int elementCapacity,
                                 int* outVertexCount, int* outElementCount)
{
	if (polygonSize < 3)
		return -1;
	TESStesselator* tess = tessNewTess(0);
	if (!tess)
		return -1;
	const float* p = points;
	for (int i = 0; i < contours; i++)
	{
		tessAddContour(tess, 2, p, sizeof(float) * 2, counts[i]);
		p += counts[i] * 2;
	}
	tessSetOption(tess, TESS_CONSTRAINED_DELAUNAY_TRIANGULATION, delaunay);
	if (!tessTesselate(tess, oddEven ? TESS_WINDING_ODD : TESS_WINDING_NONZERO, TESS_POLYGONS, polygonSize, 2, 0))
	{
		tessDeleteTess(tess);
		return -1;
	}
	int vertexCount = tessGetVertexCount(tess);
	int elementCount = tessGetElementCount(tess);
	*outVertexCount = vertexCount;
	*outElementCount = elementCount;
	if (vertexCount > vertexCapacity || elementCount > elementCapacity)
	{
		tessDeleteTess(tess);
		return 1;
	}
	const TESSreal* vertices = tessGetVertices(tess);
	const TESSindex* indices = tessGetVertexIndices(tess);
	const TESSindex* elements = tessGetElements(tess);
	for (int i = 0; i < vertexCount * 2; i++)
		outVertices[i] = vertices[i];
	for (int i = 0; outVertexIndices && i < vertexCount; i++)
		outVertexIndices[i] = indices[i] == TESS_UNDEF ? -1 : (int)indices[i];
	for (int i = 0; i < elementCount * polygonSize; i++)
		outElements[i] = elements[i];
	tessDeleteTess(tess);
	return 0;
}

// Preserve the triangle API and its refinement policy for Painter2D.
UA_TESS_API int ua_tess_triangulate(const float* points, const int* counts, int contours, int oddEven,
                                    float* outVertices, int* outVertexIndices, int vertexCapacity,
                                    int* outTriangles, int triangleCapacity, int* outVertexCount,
                                    int* outTriangleCount)
{
	return ua_tess_polygons(points, counts, contours, oddEven, 1, 3, outVertices, outVertexIndices,
	                        vertexCapacity, outTriangles, triangleCapacity, outVertexCount, outTriangleCount);
}
