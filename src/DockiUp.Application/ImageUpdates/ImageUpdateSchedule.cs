namespace DockiUp.Application.ImageUpdates
{
    /// <summary>Which projects' images are due for a check: each on its own interval, else DockiUp's
    /// (ImageUpdates:Interval, default 6h). An interval of 0 means never automatically.</summary>
    public static class ImageUpdateSchedule
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(6);

        /// <summary>DockiUp's interval from configuration; unreadable falls back to the default.</summary>
        public static TimeSpan GlobalInterval(string? configured)
            => Durations.Parse(configured) is { } d && (d == TimeSpan.Zero || d >= Durations.Minimum) ? d : DefaultInterval;

        /// <param name="lastRun">When each project was last checked by the schedule; a project not in it is due.</param>
        public static Guid[] Due(IEnumerable<(Guid Id, int? IntervalSeconds)> projects, TimeSpan global,
            IReadOnlyDictionary<Guid, DateTime> lastRun, DateTime now)
            => [.. projects
                .Select(p => (p.Id, Interval: p.IntervalSeconds is { } s ? TimeSpan.FromSeconds(s) : global))
                .Where(p => p.Interval > TimeSpan.Zero && (!lastRun.TryGetValue(p.Id, out var last) || now - last >= p.Interval))
                .Select(p => p.Id)];
    }
}
