using DockiUp.Application.Dtos;
using DockiUp.Domain;
using DockiUp.Domain.Enums;

namespace DockiUp.Application.Monitoring
{
    public static class CleanupScheduleRules
    {
        /// <summary>The most recent scheduled time at or before <paramref name="nowUtc"/>; null when off.</summary>
        public static DateTime? LastOccurrence(CleanupSchedule schedule, DateTime nowUtc)
        {
            if (schedule.Frequency == CleanupFrequency.Off) return null;
            var today = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, schedule.HourUtc, 0, 0, DateTimeKind.Utc);
            if (schedule.Frequency == CleanupFrequency.Daily)
                return today <= nowUtc ? today : today.AddDays(-1);

            var back = ((int)nowUtc.DayOfWeek - schedule.DayOfWeekUtc + 7) % 7;
            var occurrence = today.AddDays(-back);
            return occurrence <= nowUtc ? occurrence : occurrence.AddDays(-7);
        }

        /// <summary>Due when a scheduled time has passed that wasn't run yet. Times before the schedule was
        /// saved don't count, so turning it on at 15:00 for "daily at 03:00" waits for tomorrow's 03:00.</summary>
        public static bool IsDue(CleanupSchedule schedule, DateTime nowUtc)
        {
            var occurrence = LastOccurrence(schedule, nowUtc);
            if (occurrence is null) return false;
            var since = schedule.LastRunAt is { } last && last > schedule.SavedAt ? last : schedule.SavedAt;
            return occurrence > since;
        }

        /// <summary>What a scheduled run prunes. The keep-rule: stopped containers of compose projects are
        /// kept, so every project's images and volumes - including the images its latest successful
        /// deployment runs, and those of projects that are merely stopped - stay in use and survive. Images no
        /// container references at all are removed; volumes only when the schedule opts in.</summary>
        public static PruneRequest ScheduledRequest(CleanupSchedule schedule) => new(
            Containers: true,
            Images: true,
            AllImages: true,
            Networks: true,
            Volumes: schedule.PruneVolumes,
            KeepProjectContainers: true);

        public static CleanupScheduleDto ToDto(CleanupSchedule? s, Guid? nodeId) => s is null
            ? new CleanupScheduleDto(nodeId, CleanupFrequency.Off, 3, 0, false, null, null)
            : new CleanupScheduleDto(s.NodeId, s.Frequency, s.HourUtc, s.DayOfWeekUtc, s.PruneVolumes, s.LastRunAt, s.LastRunResult);

        public static string Describe(PruneResultDto r)
            => $"freed {FormatBytes(r.SpaceReclaimed)}: {r.ContainersDeleted} containers, {r.ImagesDeleted} images, {r.NetworksDeleted} networks, {r.VolumesDeleted} volumes";

        public static string FormatBytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return unit == 0 ? $"{bytes} B" : $"{value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} {units[unit]}";
        }
    }
}
