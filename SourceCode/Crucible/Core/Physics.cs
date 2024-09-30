using SharpDX;

namespace Crucible;

public static class Physics
{
  static bool BoxContainsPoint(ref BoundingBox rect, ref Vector3 dot)
  {
    var result = SharpDX.Collision.BoxContainsPoint(ref rect, ref dot);
    return result == ContainmentType.Disjoint ? false : true;
  }

  static bool LineTriangleIntersection(ref Ray ray, ref Vector3 v1, ref Vector3 v2, ref Vector3 v3, out float distance)
  {
    return SharpDX.Collision.RayIntersectsTriangle(ref ray, ref v1, ref v2, ref v3, out distance);
  }

  static bool LineTriangleIntersection(ref Ray ray, ref Vector3 v1, ref Vector3 v2, ref Vector3 v3, out Vector3 pos)
  {
    return SharpDX.Collision.RayIntersectsTriangle(ref ray, ref v1, ref v2, ref v3, out pos);
  }

  static void a()
  {
    //SharpDX.Collision.
  }
}