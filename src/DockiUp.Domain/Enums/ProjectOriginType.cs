namespace DockiUp.Domain.Enums
{
    public enum ProjectOriginType
    {
        Unknown = 0,
        Git = 1,
        Compose = 2,
        /// <summary>An existing compose project adopted in place: DockiUp manages it but did not create
        /// (and never moves or deletes) its files.</summary>
        Adopted = 3,
    }
}
