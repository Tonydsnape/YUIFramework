using System;

namespace YUIFramework
{
    /// <summary>
    /// 描述一次导航命令的元数据，在命令真正执行（出队）时构造并传给导航守卫。
    /// </summary>
    public readonly struct UINavigationRequest
    {
        public UINavigationRequest(UINavigationCommandKind kind, Type fromType, Type toType, object args)
        {
            Kind = kind;
            FromType = fromType;
            ToType = toType;
            Args = args;
        }

        /// <summary>命令类型。</summary>
        public UINavigationCommandKind Kind { get; }

        /// <summary>命令执行时的当前栈顶页面类型；可能为 null（空栈）。</summary>
        public Type FromType { get; }

        /// <summary>命令的目标页面类型；Pop/Back 时表示将要显示的上一页，可能为 null。</summary>
        public Type ToType { get; }

        /// <summary>随命令传入的参数。</summary>
        public object Args { get; }

        public override string ToString()
        {
            return $"{Kind}({FromType?.Name ?? "none"} -> {ToType?.Name ?? "none"})";
        }
    }
}
