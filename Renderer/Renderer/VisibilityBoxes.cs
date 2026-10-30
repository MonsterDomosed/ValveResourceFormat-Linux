namespace ValveResourceFormat.Renderer;

/// <summary>What an <c>info_visibility_box</c> hides, from its <c>cull_mode</c>.</summary>
public enum VisibilityBoxMode
{
    /// <summary>Hides objects entirely inside the box.</summary>
    Inside,

    /// <summary>Hides objects that touch none of the outside boxes.</summary>
    Outside,

    /// <summary>Counts as <see cref="Outside"/> when the view eye is inside the box.</summary>
    OutsideWhenEyeInside,
}

/// <summary>An oriented visibility box volume.</summary>
public sealed class VisibilityBox(VisibilityBoxMode mode, Vector3 size)
{
    private readonly Vector4[] planes = new Vector4[6];

    /// <summary>Gets what the box hides.</summary>
    public VisibilityBoxMode Mode { get; } = mode;

    /// <summary>Gets the full edge lengths along the box axes.</summary>
    public Vector3 Size { get; } = size;

    /// <summary>Gets whether the box is active.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Enables the box at a rigid world transform.</summary>
    public void Enable(Matrix4x4 transform)
    {
        if (IsEnabled)
        {
            return;
        }

        var origin = transform.Translation;
        for (var axis = 0; axis < 3; axis++)
        {
            var (direction, halfSize) = axis switch
            {
                0 => (new Vector3(transform.M11, transform.M12, transform.M13), Size.X * 0.5f),
                1 => (new Vector3(transform.M21, transform.M22, transform.M23), Size.Y * 0.5f),
                _ => (new Vector3(transform.M31, transform.M32, transform.M33), Size.Z * 0.5f),
            };

            direction = Vector3.Normalize(direction);
            planes[axis] = InwardPlane(-direction, origin + direction * halfSize);
            planes[axis + 3] = InwardPlane(direction, origin - direction * halfSize);
        }

        IsEnabled = true;
    }

    /// <summary>Disables the box.</summary>
    public void Disable() => IsEnabled = false;

    private static Vector4 InwardPlane(Vector3 normal, Vector3 point) => new(normal, -Vector3.Dot(normal, point));

    private float MinPlaneDistance(Vector3 center, Vector3 extents, float sign)
    {
        var distance = float.MaxValue;
        foreach (var plane in planes)
        {
            var normal = new Vector3(plane.X, plane.Y, plane.Z);
            var radius = Vector3.Dot(Vector3.Abs(normal), extents);
            distance = MathF.Min(distance, Vector3.Dot(normal, center) + plane.W + sign * radius);
        }

        return distance;
    }

    /// <summary>Tests whether a point is inside the box.</summary>
    public bool Contains(Vector3 point) => MinPlaneDistance(point, Vector3.Zero, 0f) >= 0f;

    /// <summary>Tests whether an axis-aligned box lies entirely inside this one.</summary>
    public bool Contains(in AABB bounds) => MinPlaneDistance(bounds.Center, bounds.Size * 0.5f, -1f) >= 0f;

    /// <summary>Tests whether an axis-aligned box reaches the inner side of every plane.</summary>
    public bool Touches(in AABB bounds) => MinPlaneDistance(bounds.Center, bounds.Size * 0.5f, 1f) >= 0f;
}

/// <summary>Splits visibility boxes for one view and culls bounds against them.</summary>
public sealed class VisibilityBoxCuller
{
    private readonly List<VisibilityBox> inside = [];
    private readonly List<VisibilityBox> outside = [];

    /// <summary>Gets whether any box is active.</summary>
    public bool IsActive => inside.Count > 0 || outside.Count > 0;

    /// <summary>Gets the number of inside boxes.</summary>
    public int InsideCount => inside.Count;

    /// <summary>Gets the number of outside boxes.</summary>
    public int OutsideCount => outside.Count;

    /// <summary>Splits enabled boxes for a view eye.</summary>
    public void Build(IReadOnlyList<VisibilityBox> boxes, Vector3? eye)
    {
        inside.Clear();
        outside.Clear();
        if (eye is not { } eyePosition)
        {
            return;
        }

        foreach (var box in boxes)
        {
            if (!box.IsEnabled)
            {
                continue;
            }

            switch (box.Mode)
            {
                case VisibilityBoxMode.Inside:
                    inside.Add(box);
                    break;
                case VisibilityBoxMode.Outside:
                    outside.Add(box);
                    break;
                case VisibilityBoxMode.OutsideWhenEyeInside when box.Contains(eyePosition):
                    outside.Add(box);
                    break;
            }
        }
    }

    /// <summary>Tests whether bounds are hidden by this view's boxes.</summary>
    public bool IsCulled(in AABB bounds)
    {
        foreach (var box in inside)
        {
            if (box.Contains(bounds))
            {
                return true;
            }
        }

        if (outside.Count == 0)
        {
            return false;
        }

        foreach (var box in outside)
        {
            if (box.Touches(bounds))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>The visibility boxes belonging to one scene.</summary>
public sealed class VisibilityBoxSet
{
    private readonly List<VisibilityBox> boxes = [];
    private Vector3? viewEye;

    /// <summary>Gets or sets whether boxes cull.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets the number of boxes in the scene.</summary>
    public int Count => boxes.Count;

    /// <summary>Gets the culler for the scene's main view.</summary>
    public VisibilityBoxCuller View { get; } = new();

    internal VisibilityBoxCuller SecondaryView { get; } = new();

    /// <summary>Adds a box to the scene.</summary>
    public void Add(VisibilityBox box) => boxes.Add(box);

    /// <summary>Removes a box from the scene.</summary>
    public void Remove(VisibilityBox box) => boxes.Remove(box);

    /// <summary>Prepares the main view culler.</summary>
    public void PrepareView(Vector3? eye)
    {
        viewEye = Enabled ? eye : null;
        View.Build(boxes, viewEye);
    }

    internal VisibilityBoxCuller PrepareSecondaryView(Vector3 eye)
    {
        SecondaryView.Build(boxes, viewEye.HasValue ? eye : null);
        return SecondaryView;
    }
}
