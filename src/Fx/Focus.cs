using Godot;

namespace DestructionRacing.Fx;

/// <summary>
/// Depth of field on a camera, focused on a point (plan §8.4: in the replays' TV and free cameras and behind the
/// menus, never while you drive). The far blur starts a little behind the point and the near blur a little in front
/// of it, both scaled with the distance, as with a real lens: the TV camera's long lens on a far car blurs the stands
/// behind it, the showroom blurs the far end of the floor.
/// </summary>
public static class Focus
{
    /// <summary>Dev A/B (<c>--ab=dof</c>): no depth of field.</summary>
    public static bool Off;

    public static void On(Camera3D camera, Vector3 point, float amount = 0.06f, bool near = true)
    {
        if (camera.Attributes is not CameraAttributesPractical a)
        {
            a = new CameraAttributesPractical();
            camera.Attributes = a;
        }
        if (Off)
        {
            a.DofBlurFarEnabled = a.DofBlurNearEnabled = false;
            return;
        }
        float d = camera.GlobalPosition.DistanceTo(point);
        a.DofBlurFarEnabled = true;
        a.DofBlurFarDistance = d + Mathf.Max(3f, d * 0.3f);
        a.DofBlurFarTransition = Mathf.Max(6f, d * 0.8f);
        a.DofBlurNearEnabled = near && d > 6f;
        a.DofBlurNearDistance = d * 0.35f;
        a.DofBlurNearTransition = d * 0.25f;
        a.DofBlurAmount = amount;
    }
}
