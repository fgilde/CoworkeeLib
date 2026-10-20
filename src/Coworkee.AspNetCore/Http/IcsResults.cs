using System.Text;
using Coworkee.Application.Calendar;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Http;

public static class IcsResults
{
    extension(Results)
    {
        /// <summary>The calendar as <c>text/calendar; charset=utf-8</c>; with a file name as a download (Content-Disposition attachment).</summary>
        public static IResult Ics(IcsCalendar calendar, string? fileName = null) =>
            Results.Bytes(Encoding.UTF8.GetBytes(calendar.ToString()), "text/calendar; charset=utf-8", fileName);
    }
}
