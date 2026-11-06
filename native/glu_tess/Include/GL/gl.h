/* Minimal GL scalar types for the CPU-only tessellator. No graphics calls are used. */
#ifndef UNBOUND_TESS_GL_TYPES_H
#define UNBOUND_TESS_GL_TYPES_H
typedef unsigned int GLenum;
typedef unsigned char GLboolean;
typedef unsigned char GLubyte;
typedef int GLint;
typedef int GLsizei;
typedef float GLfloat;
typedef double GLdouble;
typedef void GLvoid;
#define GL_FALSE 0
#define GL_LINE_LOOP 2
#define GL_TRIANGLES 4
#define GL_TRIANGLE_STRIP 5
#define GL_TRIANGLE_FAN 6
#endif
