# Clipper 6.4.2

`clipper.cs` is Angus Johnson's Clipper library, version 6.4.2 (27 February 2017), C# translation,
under the Boost Software License 1.0 (see the header of the file;
<http://www.boost.org/LICENSE_1_0.txt>). Taken from the copy Unity's `com.unity.2d.animation`
package ships, with only the namespace changed to `Unbound.Clipper`.

Unity's CompositeCollider2D merges colliders with Clipper; Unbound runs the same algorithm so the
merged outlines (their vertices, order and starting points) come out as Unity's do. The composite
pipeline that reproduces Unity 6 is in `CompositeCollider2D.cs`.
