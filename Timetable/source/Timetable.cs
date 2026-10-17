// Timetable - a native Windows (WPF) timetable viewer for Outlook / .ics calendars.
// Written for the C# 5 compiler that ships with Windows (.NET Framework 4.x); see build.ps1.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

/* ---------- model ---------- */
class ByDay { public int N; public int Wd; }
class Rule
{
    public string Freq; public int Interval = 1, Count, Wkst = 1, SetPos;
    public DateTime? Until; public List<ByDay> ByDay; public List<int> ByMonthDay, ByMonth;
}
class Master
{
    public string Uid = "", Title, Loc, Desc; public bool AllDay, Cancelled, Utc; public TimeZoneInfo Tz;
    public DateTime Nv; public TimeSpan Dur; public Rule R; public HashSet<long> Ex = new HashSet<long>(); public long? Rid;
    // worked out from the title by Ics.Classify: "[CODE]Subject [Type]Teacher" or a plain title
    public string Subject = "", Code = "", Kind = "", Teacher = "", Key = ""; public bool IsExam, IsResit;
}
class Subj
{
    public string Name; public Color C; public Occ Next;
    public List<Occ> Exams = new List<Occ>(), Resits = new List<Occ>(), Sessions = new List<Occ>();
}
class Occ { public Master M; public DateTime S, E; }
class Source
{
    public string Id, Type = "file", Url = "", Name = "", Error = ""; public DateTime Updated = DateTime.Now;
    public List<Master> Masters = new List<Master>();
}
class DT
{
    public bool AllDay, Utc; public DateTime Nv; public TimeZoneInfo Tz;
    public DateTime Local() { return Ics.ToLocal(Nv, AllDay ? false : Utc, AllDay ? null : Tz); }
}
class RawEvent
{
    public Dictionary<string, string> V = new Dictionary<string, string>();
    public Dictionary<string, Dictionary<string, string>> P = new Dictionary<string, Dictionary<string, string>>();
    public List<KeyValuePair<string, Dictionary<string, string>>> Ex = new List<KeyValuePair<string, Dictionary<string, string>>>();
}

/* ---------- ICS parsing and recurrence ---------- */
static class Ics
{
    static readonly Dictionary<string, int> WD = new Dictionary<string, int> { { "SU", 0 }, { "MO", 1 }, { "TU", 2 }, { "WE", 3 }, { "TH", 4 }, { "FR", 5 }, { "SA", 6 } };
    static readonly Dictionary<string, TimeZoneInfo> zoneCache = new Dictionary<string, TimeZoneInfo>();
    // Outlook writes Windows zone names (understood natively); Apple / Google write IANA names.
    static readonly Dictionary<string, string> IANA = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        {"UTC","UTC"},{"Etc/UTC","UTC"},{"GMT","UTC"},{"Etc/GMT","UTC"},
        {"Europe/Paris","Romance Standard Time"},{"Europe/Brussels","Romance Standard Time"},{"Europe/Madrid","Romance Standard Time"},{"Europe/Copenhagen","Romance Standard Time"},
        {"Europe/Berlin","W. Europe Standard Time"},{"Europe/Amsterdam","W. Europe Standard Time"},{"Europe/Zurich","W. Europe Standard Time"},{"Europe/Luxembourg","W. Europe Standard Time"},
        {"Europe/Rome","W. Europe Standard Time"},{"Europe/Vienna","W. Europe Standard Time"},{"Europe/Stockholm","W. Europe Standard Time"},{"Europe/Oslo","W. Europe Standard Time"},{"Europe/Monaco","W. Europe Standard Time"},
        {"Europe/London","GMT Standard Time"},{"Europe/Dublin","GMT Standard Time"},{"Europe/Lisbon","GMT Standard Time"},
        {"Europe/Warsaw","Central European Standard Time"},{"Europe/Prague","Central Europe Standard Time"},{"Europe/Budapest","Central Europe Standard Time"},
        {"Europe/Helsinki","FLE Standard Time"},{"Europe/Kiev","FLE Standard Time"},{"Europe/Kyiv","FLE Standard Time"},{"Europe/Athens","GTB Standard Time"},{"Europe/Bucharest","GTB Standard Time"},
        {"Europe/Istanbul","Turkey Standard Time"},{"Europe/Moscow","Russian Standard Time"},
        {"America/New_York","Eastern Standard Time"},{"America/Toronto","Eastern Standard Time"},{"America/Chicago","Central Standard Time"},{"America/Denver","Mountain Standard Time"},
        {"America/Phoenix","US Mountain Standard Time"},{"America/Los_Angeles","Pacific Standard Time"},{"America/Vancouver","Pacific Standard Time"},{"America/Halifax","Atlantic Standard Time"},
        {"America/Anchorage","Alaskan Standard Time"},{"Pacific/Honolulu","Hawaiian Standard Time"},{"America/Sao_Paulo","E. South America Standard Time"},{"America/Mexico_City","Central Standard Time (Mexico)"},
        {"Asia/Kolkata","India Standard Time"},{"Asia/Calcutta","India Standard Time"},{"Asia/Shanghai","China Standard Time"},{"Asia/Hong_Kong","China Standard Time"},
        {"Asia/Singapore","Singapore Standard Time"},{"Asia/Tokyo","Tokyo Standard Time"},{"Asia/Seoul","Korea Standard Time"},{"Asia/Dubai","Arabian Standard Time"},
        {"Asia/Jerusalem","Israel Standard Time"},{"Asia/Bangkok","SE Asia Standard Time"},{"Asia/Karachi","Pakistan Standard Time"},{"Asia/Riyadh","Arab Standard Time"},
        {"Australia/Sydney","AUS Eastern Standard Time"},{"Australia/Melbourne","AUS Eastern Standard Time"},{"Australia/Brisbane","E. Australia Standard Time"},{"Australia/Perth","W. Australia Standard Time"},
        {"Pacific/Auckland","New Zealand Standard Time"},{"Africa/Johannesburg","South Africa Standard Time"},{"Africa/Cairo","Egypt Standard Time"},{"Africa/Casablanca","Morocco Standard Time"}
    };

    static TimeZoneInfo ResolveZone(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        id = id.Trim('"');
        TimeZoneInfo z;
        if (zoneCache.TryGetValue(id, out z)) return z;
        z = null;
        var tries = new List<string> { id };
        string w; if (IANA.TryGetValue(id, out w)) tries.Add(w);
        foreach (var c in tries) { try { z = TimeZoneInfo.FindSystemTimeZoneById(c); break; } catch { } }
        zoneCache[id] = z; return z;
    }

    // nv is a wall-clock time in the event's own zone; the result is the same instant in this PC's local time.
    public static DateTime ToLocal(DateTime nv, bool utc, TimeZoneInfo tz)
    {
        nv = DateTime.SpecifyKind(nv, DateTimeKind.Unspecified);
        try
        {
            if (utc) return DateTime.SpecifyKind(DateTime.SpecifyKind(nv, DateTimeKind.Utc).ToLocalTime(), DateTimeKind.Unspecified);
            if (tz == null) return nv;
            if (tz.IsInvalidTime(nv)) nv = nv.AddHours(1);
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(nv, tz).ToLocalTime(), DateTimeKind.Unspecified);
        }
        catch { return nv; }
    }
    static DateTime ToLocal(Master m, DateTime nv) { return ToLocal(nv, m.Utc, m.Tz); }

    static string Unesc(string s)
    {
        if (s == null) return "";
        return Regex.Replace(Regex.Replace(s, @"\\[nN]", "\n"), @"\\([,;\\])", "$1").Trim();
    }

    static void ParseLine(string l, out string name, out Dictionary<string, string> prm, out string value)
    {
        bool q = false; int i = 0;
        for (; i < l.Length; i++) { char c = l[i]; if (c == '"') q = !q; else if (c == ':' && !q) break; }
        string head = l.Substring(0, i);
        value = i < l.Length ? l.Substring(i + 1) : "";
        var parts = head.Split(';');
        name = parts[0].Trim().ToUpperInvariant();
        prm = new Dictionary<string, string>();
        for (int k = 1; k < parts.Length; k++)
        {
            int e = parts[k].IndexOf('=');
            if (e > 0) prm[parts[k].Substring(0, e).ToUpperInvariant()] = parts[k].Substring(e + 1);
        }
    }

    static DT ParseDT(string v, Dictionary<string, string> prm)
    {
        var m = Regex.Match(v.Trim(), @"^(\d{4})(\d{2})(\d{2})(?:T(\d{2})(\d{2})(\d{2})?)?(Z)?");
        if (!m.Success) return null;
        try
        {
            var d = new DT();
            d.AllDay = !m.Groups[4].Success;
            int h = d.AllDay ? 0 : int.Parse(m.Groups[4].Value), mi = d.AllDay ? 0 : int.Parse(m.Groups[5].Value);
            int s = m.Groups[6].Success ? Math.Min(59, int.Parse(m.Groups[6].Value)) : 0;
            d.Nv = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), h, mi, s);
            d.Utc = m.Groups[7].Success;
            string tzid;
            if (!d.AllDay && !d.Utc && prm != null && prm.TryGetValue("TZID", out tzid)) d.Tz = ResolveZone(tzid);
            return d;
        }
        catch { return null; }
    }

    static TimeSpan ParseDur(string v)
    {
        var m = Regex.Match(v.Trim(), @"^([+-])?P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?");
        if (!m.Success) return TimeSpan.Zero;
        Func<int, double> g = i => m.Groups[i].Success ? double.Parse(m.Groups[i].Value) : 0;
        var t = TimeSpan.FromDays(g(2) * 7 + g(3)) + TimeSpan.FromHours(g(4)) + TimeSpan.FromMinutes(g(5)) + TimeSpan.FromSeconds(g(6));
        return m.Groups[1].Value == "-" ? -t : t;
    }

    static List<int> Ints(string s)
    {
        var l = new List<int>();
        foreach (var p in s.Split(',')) { int n; if (int.TryParse(p.Trim(), out n) && n != 0) l.Add(n); }
        return l.Count > 0 ? l : null;
    }

    static Rule ParseRule(string v)
    {
        var r = new Dictionary<string, string>();
        foreach (var kv in v.Split(';')) { int e = kv.IndexOf('='); if (e > 0) r[kv.Substring(0, e).Trim().ToUpperInvariant()] = kv.Substring(e + 1).Trim(); }
        if (!r.ContainsKey("FREQ")) return null;
        var o = new Rule { Freq = r["FREQ"].ToUpperInvariant() };
        int n;
        if (r.ContainsKey("INTERVAL") && int.TryParse(r["INTERVAL"], out n)) o.Interval = Math.Max(1, n);
        if (r.ContainsKey("COUNT") && int.TryParse(r["COUNT"], out n)) o.Count = n;
        if (r.ContainsKey("WKST") && WD.ContainsKey(r["WKST"].ToUpperInvariant())) o.Wkst = WD[r["WKST"].ToUpperInvariant()];
        if (r.ContainsKey("UNTIL")) { var u = ParseDT(r["UNTIL"], null); if (u != null) o.Until = u.AllDay ? u.Nv.AddDays(1).AddSeconds(-1) : u.Local(); }
        if (r.ContainsKey("BYDAY"))
        {
            var l = new List<ByDay>();
            foreach (var s in r["BYDAY"].Split(','))
            {
                var m = Regex.Match(s.Trim().ToUpperInvariant(), @"^([+-]?\d+)?([A-Z]{2})$");
                if (m.Success && WD.ContainsKey(m.Groups[2].Value))
                    l.Add(new ByDay { N = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0, Wd = WD[m.Groups[2].Value] });
            }
            if (l.Count > 0) o.ByDay = l;
        }
        if (r.ContainsKey("BYMONTHDAY")) o.ByMonthDay = Ints(r["BYMONTHDAY"]);
        if (r.ContainsKey("BYMONTH")) o.ByMonth = Ints(r["BYMONTH"]);
        if (r.ContainsKey("BYSETPOS")) { var sp = Ints(r["BYSETPOS"]); if (sp != null) o.SetPos = sp[0]; }
        return o;
    }

    public static List<Master> Parse(string text, out string calName)
    {
        calName = "";
        text = Regex.Replace(text.Replace("\r\n", "\n").Replace('\r', '\n'), "\n[ \t]", "");
        var masters = new List<Master>();
        RawEvent ev = null; int depth = 0;
        foreach (var raw in text.Split('\n'))
        {
            if (raw.Length == 0) continue;
            string name, value; Dictionary<string, string> prm;
            ParseLine(raw, out name, out prm, out value);
            if (name == "BEGIN")
            {
                if (value.Trim().ToUpperInvariant() == "VEVENT") { ev = new RawEvent(); depth = 0; }
                else if (ev != null) depth++;
                continue;
            }
            if (name == "END")
            {
                if (ev != null && depth > 0) { depth--; continue; }
                if (ev != null && value.Trim().ToUpperInvariant() == "VEVENT") { var m = Finish(ev); if (m != null) masters.Add(m); ev = null; }
                continue;
            }
            if (ev == null) { if (name == "X-WR-CALNAME") calName = Unesc(value); continue; }
            if (depth > 0) continue; // inside an alarm
            if (name == "EXDATE") ev.Ex.Add(new KeyValuePair<string, Dictionary<string, string>>(value, prm));
            else { ev.V[name] = value; ev.P[name] = prm; }
        }
        // a changed single occurrence replaces the matching occurrence of its repeating event
        var byUid = new Dictionary<string, Master>();
        foreach (var m in masters) if (m.R != null && m.Uid != "") byUid[m.Uid] = m;
        foreach (var m in masters) if (m.Rid.HasValue && byUid.ContainsKey(m.Uid)) byUid[m.Uid].Ex.Add(m.Rid.Value);
        return masters.Where(m => !m.Cancelled).ToList();
    }

    static string Get(RawEvent e, string k) { string v; return e.V.TryGetValue(k, out v) ? v : null; }

    static readonly Regex Structured = new Regex(@"^\s*\[([^\]]*)\]\s*(.*?)\s*(?:\[([^\]]*)\]\s*(.*))?$", RegexOptions.Singleline);
    static readonly Regex ExamRx = new Regex(@"(?<![\p{L}])(examens?|exams?|partiels?|contr[oô]les?|ds|dst|midterms?|quiz|[ée]valuations?|soutenances?)(?![\p{L}])", RegexOptions.IgnoreCase);
    static readonly Regex ResitRx = new Regex(@"(?<![\p{L}])(rattrapages?|resits?|retakes?|seconde session|session 2)(?![\p{L}])", RegexOptions.IgnoreCase);

    // Splits school-style titles such as "[CODE]Subject [TD]Teacher" and spots exams by keyword.
    // Plain titles still work: the whole title is the subject and the keywords are looked for in it.
    static void Classify(Master m)
    {
        m.Subject = m.Title;
        var x = Structured.Match(m.Title);
        if (x.Success && x.Groups[2].Value.Trim() != "")
        {
            m.Code = x.Groups[1].Value.Trim(); m.Subject = x.Groups[2].Value.Trim();
            m.Kind = x.Groups[3].Value.Trim(); m.Teacher = x.Groups[4].Value.Trim();
            if (m.Kind.ToUpperInvariant() == "NR") m.Kind = "";
        }
        string probe = m.Kind != "" ? m.Kind : m.Subject;
        m.IsResit = ResitRx.IsMatch(probe); m.IsExam = m.IsResit || ExamRx.IsMatch(probe);
        m.Key = (m.Code != "" ? m.Code : m.Subject).ToLowerInvariant().Trim();
    }

    static Master Finish(RawEvent e)
    {
        if (Get(e, "DTSTART") == null) return null;
        var s = ParseDT(e.V["DTSTART"], e.P["DTSTART"]);
        if (s == null) return null;
        var dur = TimeSpan.Zero;
        if (Get(e, "DTEND") != null)
        {
            var x = ParseDT(e.V["DTEND"], e.P["DTEND"]);
            if (x != null) dur = (s.AllDay || (s.Utc == x.Utc && s.Tz == x.Tz)) ? x.Nv - s.Nv : x.Local() - s.Local();
        }
        else if (Get(e, "DURATION") != null) dur = ParseDur(e.V["DURATION"]);
        if (dur <= TimeSpan.Zero) dur = s.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
        var title = Unesc(Get(e, "SUMMARY"));
        var m = new Master
        {
            Uid = Get(e, "UID") ?? "", Title = title == "" ? "(No title)" : title, Loc = Unesc(Get(e, "LOCATION")), Desc = Unesc(Get(e, "DESCRIPTION")),
            AllDay = s.AllDay, Utc = s.AllDay ? false : s.Utc, Tz = s.AllDay ? null : s.Tz, Nv = s.Nv, Dur = dur,
            R = Get(e, "RRULE") != null ? ParseRule(e.V["RRULE"]) : null,
            Cancelled = (Get(e, "STATUS") ?? "").Trim().ToUpperInvariant() == "CANCELLED"
        };
        Classify(m);
        foreach (var x in e.Ex)
            foreach (var v in x.Key.Split(',')) { var d = ParseDT(v, x.Value); if (d != null) m.Ex.Add(d.Local().Ticks); }
        if (Get(e, "RECURRENCE-ID") != null) { var d = ParseDT(e.V["RECURRENCE-ID"], e.P["RECURRENCE-ID"]); if (d != null) m.Rid = d.Local().Ticks; }
        return m;
    }

    static int NthWd(int y, int mo, int wd, int n)
    {
        int L = DateTime.DaysInMonth(y, mo);
        if (n > 0) { int d = 1 + (wd - (int)new DateTime(y, mo, 1).DayOfWeek + 7) % 7 + (n - 1) * 7; return d <= L ? d : 0; }
        int x = L - ((int)new DateTime(y, mo, L).DayOfWeek - wd + 7) % 7 + (n + 1) * 7; return x >= 1 ? x : 0;
    }

    static List<int> MonthDays(Rule r, int y, int mo, int dflt)
    {
        int L = DateTime.DaysInMonth(y, mo); var ds = new List<int>();
        if (r.ByDay != null)
        {
            foreach (var b in r.ByDay)
            {
                if (b.N != 0) { int d = NthWd(y, mo, b.Wd, b.N); if (d > 0) ds.Add(d); }
                else for (int d = 1; d <= L; d++) if ((int)new DateTime(y, mo, d).DayOfWeek == b.Wd) ds.Add(d);
            }
            if (r.ByMonthDay != null) ds = ds.Where(d => r.ByMonthDay.Contains(d) || r.ByMonthDay.Contains(d - L - 1)).ToList();
        }
        else if (r.ByMonthDay != null) ds = r.ByMonthDay.Select(d => d < 0 ? L + 1 + d : d).ToList();
        else ds.Add(dflt);
        ds = ds.Distinct().Where(d => d >= 1 && d <= L).OrderBy(d => d).ToList();
        if (r.SetPos != 0 && ds.Count > 0)
        {
            int i = r.SetPos > 0 ? r.SetPos - 1 : ds.Count + r.SetPos;
            ds = (i >= 0 && i < ds.Count) ? new List<int> { ds[i] } : new List<int>();
        }
        return ds;
    }

    static IEnumerable<DateTime> Gen(Master m)
    {
        var r = m.R; var nv = m.Nv; var d0 = nv.Date; var tod = nv.TimeOfDay; const int CAP = 4000;
        if (r.Freq == "DAILY")
        {
            for (int i = 0; i < CAP * 4; i++)
            {
                var c = nv.AddDays((double)i * r.Interval);
                if (c.Year > 9000) yield break;
                if (r.ByDay != null && !r.ByDay.Any(b => b.Wd == (int)c.DayOfWeek)) continue;
                yield return c;
            }
        }
        else if (r.Freq == "WEEKLY")
        {
            var src = r.ByDay != null ? r.ByDay.Select(b => b.Wd).ToList() : new List<int> { (int)d0.DayOfWeek };
            var offs = src.Select(w => (w - r.Wkst + 7) % 7).Distinct().OrderBy(x => x).ToList();
            var ws = d0.AddDays(-(((int)d0.DayOfWeek - r.Wkst + 7) % 7));
            for (int w = 0; w < CAP; w++)
            {
                if (ws.Year + (long)w * r.Interval / 52 > 9000) yield break;
                foreach (var o in offs) { var c = ws.AddDays((double)w * r.Interval * 7 + o) + tod; if (c >= nv) yield return c; }
            }
        }
        else if (r.Freq == "MONTHLY")
        {
            for (int i = 0; i < CAP; i++)
            {
                long t = d0.Month - 1 + (long)i * r.Interval; int y = d0.Year + (int)(t / 12), mo = (int)(t % 12) + 1;
                if (y > 9000) yield break;
                if (r.ByMonth != null && !r.ByMonth.Contains(mo)) continue;
                foreach (var d in MonthDays(r, y, mo, d0.Day)) { var c = new DateTime(y, mo, d) + tod; if (c >= nv) yield return c; }
            }
        }
        else if (r.Freq == "YEARLY")
        {
            var months = r.ByMonth != null ? r.ByMonth.Where(x => x >= 1 && x <= 12).OrderBy(x => x).ToList() : new List<int> { d0.Month };
            for (int i = 0; i < 400; i++)
            {
                int y = d0.Year + i * r.Interval;
                if (y > 9000) yield break;
                foreach (var mo in months)
                    foreach (var d in MonthDays(r, y, mo, d0.Day)) { var c = new DateTime(y, mo, d) + tod; if (c >= nv) yield return c; }
            }
        }
        else yield return nv;
    }

    public static void Expand(Master m, DateTime from, DateTime to, List<Occ> o)
    {
        try
        {
            if (m.R == null)
            {
                var s = ToLocal(m, m.Nv); var e = ToLocal(m, m.Nv + m.Dur);
                if (e <= s) e = s.AddMinutes(30);
                if (s < to && e > from) o.Add(new Occ { M = m, S = s, E = e });
                return;
            }
            int n = 0;
            foreach (var nv in Gen(m))
            {
                if (m.R.Count > 0 && ++n > m.R.Count) break;
                if (nv + m.Dur + TimeSpan.FromDays(2) < from) continue; // far before the window: skip the conversion
                var s = ToLocal(m, nv);
                if (m.R.Until.HasValue && s > m.R.Until.Value) break;
                if (s >= to) break;
                var e = ToLocal(m, nv + m.Dur);
                if (e <= s) e = s.AddMinutes(30);
                if (e > from && !m.Ex.Contains(s.Ticks)) o.Add(new Occ { M = m, S = s, E = e });
            }
        }
        catch { }
    }
}

/* ---------- a panel that places children by fractions of its own size ---------- */
class FracPanel : Panel
{
    public int Hours; public Brush LineBrush, Fill; public bool LeftLine;
    readonly Dictionary<UIElement, Rect> rects = new Dictionary<UIElement, Rect>();
    readonly Dictionary<UIElement, int> modes = new Dictionary<UIElement, int>();
    // mode 0: block (x, y, w, h as fractions)   1: right-aligned label centred on y   2: full-width marker centred on y
    public void Add(UIElement e, Rect r, int mode) { rects[e] = r; modes[e] = mode; Children.Add(e); }

    protected override Size MeasureOverride(Size a)
    {
        double w = double.IsInfinity(a.Width) ? 0 : a.Width, h = double.IsInfinity(a.Height) ? 0 : a.Height;
        foreach (UIElement c in Children)
        {
            var r = rects[c];
            if (modes[c] == 0) c.Measure(new Size(Math.Max(0, r.Width * w), Math.Max(20, r.Height * h)));
            else c.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        return new Size(0, 0);
    }
    protected override Size ArrangeOverride(Size f)
    {
        foreach (UIElement c in Children)
        {
            var r = rects[c]; int mode = modes[c];
            if (mode == 0) c.Arrange(new Rect(r.X * f.Width, r.Y * f.Height, Math.Max(0, r.Width * f.Width), Math.Max(20, r.Height * f.Height)));
            else if (mode == 1) c.Arrange(new Rect(0, r.Y * f.Height - c.DesiredSize.Height / 2, Math.Max(0, f.Width - 9), c.DesiredSize.Height));
            else c.Arrange(new Rect(-5, r.Y * f.Height - 5, f.Width + 5, 10));
        }
        return f;
    }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (Fill != null) dc.DrawRectangle(Fill, null, new Rect(0, 0, w, h));
        if (LineBrush == null || Hours <= 0) return;
        var pen = new Pen(LineBrush, 1); pen.Freeze();
        for (int i = 0; i < Hours; i++) { double y = Math.Round(i * h / Hours) + .5; dc.DrawLine(pen, new Point(0, y), new Point(w, y)); }
        if (LeftLine) dc.DrawLine(pen, new Point(.5, 0), new Point(.5, h));
    }
}

class Theme
{
    public bool Dark; public Color Bg, Panel, Solid, Ink, Mute, Line, Hover, Accent, B1, B2, B3; public double EvMix, EvInk;
    static Color C(string s) { return (Color)ColorConverter.ConvertFromString(s); }
    public static Theme Make(bool dark)
    {
        if (!dark) return new Theme { Bg = C("#f3f2f8"), Panel = C("#B8FFFFFF"), Solid = C("#ffffff"), Ink = C("#1c1c1e"), Mute = C("#8a8a92"), Line = C("#1C3C3C43"), Hover = C("#123C3C43"), Accent = C("#ff3b30"), B1 = C("#ffd0e0"), B2 = C("#cfe0ff"), B3 = C("#ddd2ff"), EvMix = .18, EvInk = .62 };
        return new Theme { Dark = true, Bg = C("#0d0d11"), Panel = C("#B01C1C22"), Solid = C("#1c1c22"), Ink = C("#f2f2f7"), Mute = C("#8e8e99"), Line = C("#16FFFFFF"), Hover = C("#16FFFFFF"), Accent = C("#ff453a"), B1 = C("#3a1530"), B2 = C("#12264d"), B3 = C("#2a1c55"), EvMix = .26, EvInk = .30 };
    }
}

class Piece { public Occ O; public double A, B, VB; public int C, N; }

/* ---------- the window ---------- */
class MainWindow : Window
{
    static readonly string[] PAL = { "#ff5e57", "#ff9f43", "#f5b301", "#2ecc71", "#1fc8b4", "#3fa9f5", "#4b7bec", "#a55eea", "#f368a8", "#7f8fa6", "#00a8cc", "#e17055", "#6c5ce7", "#00a86b", "#d6336c", "#b7950b" };
    static readonly FontFamily Font = new FontFamily("Segoe UI");

    readonly string dataDir; readonly string shotPath; readonly bool shotDemo; readonly string forcedView;
    readonly List<Source> sources = new List<Source>();
    readonly Dictionary<string, List<Occ>> cache = new Dictionary<string, List<Occ>>();
    string view = "week", theme = "auto", overlayKind, urlText = ""; int days = 7; DateTime cursor = DateTime.Today;
    readonly Grid root = new Grid(), bgLayer = new Grid(), content = new Grid(), overlay = new Grid();
    readonly Border toast = new Border(); readonly DispatcherTimer toastTimer = new DispatcherTimer();
    Theme th; ScrollViewer bodyScroll; TextBox urlBox;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow(string[] args)
    {
        dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Timetable");
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--shot" && i + 1 < args.Length) shotPath = args[++i];
            else if (args[i] == "--data" && i + 1 < args.Length) dataDir = args[++i];
            else if (args[i] == "--demo") shotDemo = true;
            else if (args[i] == "--dark") theme = "dark";
            else if (args[i] == "--light") theme = "light";
            else if (args[i] == "--view" && i + 1 < args.Length) forcedView = args[++i];
        }
        Directory.CreateDirectory(dataDir);
        Title = "Timetable"; Width = 1320; Height = 860; MinWidth = 980; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Font; UseLayoutRounding = true; SnapsToDevicePixels = true; AllowDrop = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        root.Children.Add(bgLayer); root.Children.Add(content); root.Children.Add(overlay); root.Children.Add(toast);
        toast.Visibility = Visibility.Collapsed;
        Content = root;

        string forced = theme;
        LoadState();
        if (forced != "auto") theme = forced;
        if (forcedView != null) view = forcedView;
        AssignColors();
        Render();

        SourceInitialized += (s, e) => ApplyChrome();
        PreviewKeyDown += OnKey;
        Drop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null) AddFiles(f); };
        toastTimer.Interval = TimeSpan.FromSeconds(3.5);
        toastTimer.Tick += (s, e) => { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; };
        ExportWallpaper();
        var tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) }; tick.Tick += (s, e) => { ExportWallpaper(); if (IsVisible) Render(); }; tick.Start();
        var sync = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) }; sync.Tick += (s, e) => SyncAll(); sync.Start();
        if (shotPath != null) ContentRendered += (s, e) => Dispatcher.BeginInvoke(new Action(Shot), DispatcherPriority.ApplicationIdle);
        else SyncAll();
    }

    void Shot()
    {
        if (shotDemo) { Demo(); UpdateLayout(); }
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(shotPath)) enc.Save(fs);
            Close();
        }), DispatcherPriority.ApplicationIdle);
    }

    /* ----- storage ----- */
    void LoadState()
    {
        try
        {
            foreach (var l in File.ReadAllLines(Path.Combine(dataDir, "prefs.txt")))
            {
                var p = l.Split(new[] { '=' }, 2); if (p.Length < 2) continue;
                if (p[0] == "view" && (p[1] == "day" || p[1] == "week" || p[1] == "exams")) view = p[1];
                if (p[0] == "days") days = p[1] == "5" ? 5 : 7;
                if (p[0] == "theme") theme = p[1];
            }
        }
        catch { }
        try
        {
            foreach (var l in File.ReadAllLines(Path.Combine(dataDir, "sources.txt")))
            {
                var p = l.Split('\t'); if (p.Length < 5) continue;
                var s = new Source { Id = p[0], Type = p[1], Url = p[3], Name = p[4] };
                long t; if (long.TryParse(p[2], out t)) s.Updated = new DateTime(t);
                try { string n; s.Masters = Ics.Parse(File.ReadAllText(Path.Combine(dataDir, s.Id + ".ics"), Encoding.UTF8), out n); } catch { }
                sources.Add(s);
            }
        }
        catch { }
    }
    void SaveState()
    {
        try
        {
            File.WriteAllLines(Path.Combine(dataDir, "prefs.txt"), new[] { "view=" + view, "days=" + days, "theme=" + theme });
            File.WriteAllLines(Path.Combine(dataDir, "sources.txt"), sources.Select(s => string.Join("\t", s.Id, s.Type, s.Updated.Ticks.ToString(), s.Url, s.Name.Replace('\t', ' '))));
        }
        catch { }
    }

    /* ----- data ----- */
    List<Occ> Occs(DateTime from, DateTime to)
    {
        string k = from.Ticks + "|" + to.Ticks; List<Occ> o;
        if (cache.TryGetValue(k, out o)) return o;
        o = new List<Occ>();
        foreach (var s in sources) foreach (var m in s.Masters) Ics.Expand(m, from, to, o);
        o = o.OrderBy(x => x.S).ThenByDescending(x => x.E).ToList();
        if (cache.Count > 40) cache.Clear();
        cache[k] = o; return o;
    }
    static bool IsLong(Occ o) { return o.M.AllDay || (o.E - o.S).TotalHours >= 24; }
    static Color ColorOf(Master m)
    {
        int i; if (colorMap.TryGetValue(m.Key, out i)) return (Color)ColorConverter.ConvertFromString(PAL[i % PAL.Length]);
        uint h = 0; foreach (char ch in m.Key) h = unchecked(h * 31 + ch);
        return (Color)ColorConverter.ConvertFromString(PAL[h % PAL.Length]);
    }
    // The subjects with the most events each get their own colour, so two big subjects never look alike.
    static readonly Dictionary<string, int> colorMap = new Dictionary<string, int>();
    void AssignColors()
    {
        colorMap.Clear(); int i = 0;
        foreach (var g in sources.SelectMany(s => s.Masters).GroupBy(m => m.Key).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)) colorMap[g.Key] = i++;
    }
    static string Room(Master m) { return Regex.Replace(Regex.Replace(m.Loc, @"\s*\(\d+\)", ""), @"\s*,\s*", ", "); }
    static string KindLabel(Master m) { return m.IsResit ? "Resit" : m.IsExam ? "Exam" : m.Kind; }
    static string Meta(params string[] parts) { return string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p))); }
    void SetView(string v) { view = v; bodyScroll = null; SaveState(); Render(); }
    /* ----- live wallpaper: hand the calendars to the Lively wallpaper page as data.js ----- */
    readonly Dictionary<string, string> exported = new Dictionary<string, string>();
    static string Js(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c < ' ' || c == (char)0x2028 || c == (char)0x2029 || c == '<') sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
    // Every folder holding a copy of the wallpaper: the one beside the app, and any copy Lively made in its library.
    IEnumerable<string> WallpaperFolders()
    {
        var found = new List<string>();
        try
        {
            string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wallpaper");
            if (Directory.Exists(beside)) found.Add(beside);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roots = new List<string> { Path.Combine(local, "Lively Wallpaper", "Library", "wallpapers") };
            string pk = Path.Combine(local, "Packages");
            if (Directory.Exists(pk))
                foreach (var d in Directory.GetDirectories(pk, "*LivelyWallpaper*"))
                    roots.Add(Path.Combine(d, "LocalCache", "Local", "Lively Wallpaper", "Library", "wallpapers"));
            foreach (var r in roots)
                if (Directory.Exists(r))
                    foreach (var d in Directory.GetDirectories(r))
                        if (File.Exists(Path.Combine(d, "timetable-wallpaper.txt"))) found.Add(d);
        }
        catch { }
        return found;
    }
    void ExportWallpaper()
    {
        if (shotPath != null) return;
        try
        {
            var folders = WallpaperFolders().ToList();
            if (folders.Count == 0) return;
            string stamp = string.Join(",", sources.Select(s => s.Id + ":" + s.Updated.Ticks));
            string js = null;
            foreach (var f in folders)
            {
                string target = Path.Combine(f, "data.js"), done;
                if (File.Exists(target) && exported.TryGetValue(f, out done) && done == stamp) continue;
                if (js == null)
                {
                    var sb = new StringBuilder("window.__ttData&&window.__ttData({generated:" + Js(stamp) + ",sources:[");
                    bool first = true;
                    foreach (var s in sources)
                    {
                        string text; try { text = File.ReadAllText(Path.Combine(dataDir, s.Id + ".ics"), Encoding.UTF8); } catch { continue; }
                        sb.Append(first ? "" : ",").Append("{name:").Append(Js(s.Name)).Append(",text:").Append(Js(text)).Append("}"); first = false;
                    }
                    js = sb.Append("]});").ToString();
                }
                try { File.WriteAllText(target + ".tmp", js, new UTF8Encoding(false)); File.Copy(target + ".tmp", target, true); File.Delete(target + ".tmp"); exported[f] = stamp; } catch { }
            }
        }
        catch { }
    }

    void Changed() { cache.Clear(); AssignColors(); SaveState(); ExportWallpaper(); Render(); if (overlayKind == "src") ShowSources(); }

    int AddText(string text, string name, Source s, string type, string url)
    {
        if (text.IndexOf("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) < 0) throw new Exception("That is not a calendar (.ics) file");
        string calName; var masters = Ics.Parse(text.TrimStart('﻿'), out calName);
        if (s == null) { s = new Source { Id = Guid.NewGuid().ToString("N").Substring(0, 10) }; sources.Add(s); }
        s.Type = type; s.Url = url ?? "";
        if (string.IsNullOrEmpty(s.Name)) s.Name = calName != "" ? calName : (string.IsNullOrEmpty(name) ? "Calendar" : name);
        s.Masters = masters; s.Updated = DateTime.Now; s.Error = "";
        try { File.WriteAllText(Path.Combine(dataDir, s.Id + ".ics"), text, Encoding.UTF8); } catch { }
        return masters.Count;
    }
    static string Download(string url)
    {
        url = Regex.Replace(url.Trim(), "^webcals?:", "https:", RegexOptions.IgnoreCase);
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288; // TLS 1.2 / 1.3
        using (var wc = new WebClient())
        {
            wc.Headers["User-Agent"] = "Timetable/1.0";
            return Encoding.UTF8.GetString(wc.DownloadData(url));
        }
    }
    async Task SyncSource(Source s, bool quiet)
    {
        try { string url = s.Url; string text = await Task.Run(() => Download(url)); AddText(text, s.Name, s, "url", s.Url); }
        catch (Exception ex)
        {
            s.Error = ex is WebException ? "Could not download - check the link and your connection" : ex.Message;
            if (!quiet) Toast(s.Error);
        }
        Changed();
    }
    void SyncAll() { foreach (var s in sources.Where(x => x.Type == "url").ToList()) { var t = SyncSource(s, true); } }
    void AddFiles(IEnumerable<string> files)
    {
        int n = 0;
        bool replaced = false;
        foreach (var f in files)
        {
            // a newer file with the same name replaces the one added before instead of doubling it
            string fn = Path.GetFileNameWithoutExtension(f);
            var old = sources.FirstOrDefault(s => s.Type == "file" && s.Url == fn);
            try { n += AddText(File.ReadAllText(f, Encoding.UTF8), fn, old, "file", fn); if (old != null) replaced = true; }
            catch (Exception ex) { Toast(ex.Message); }
        }
        if (n > 0) Toast(replaced ? "Calendar updated · " + n + " events" : n + " events added");
        Changed();
    }
    async void AddUrl()
    {
        string u = (urlBox != null ? urlBox.Text : urlText).Trim();
        if (!Regex.IsMatch(u, "^(https?|webcals?)://", RegexOptions.IgnoreCase)) { Toast("Paste a link that starts with https://"); return; }
        Toast("Downloading…");
        try
        {
            string text = await Task.Run(() => Download(u));
            int n = AddText(text, "Outlook calendar", null, "url", u);
            urlText = ""; Toast(n + " events synced"); Changed();
        }
        catch (Exception ex) { Toast(ex is WebException ? "Could not download - check the link and your connection" : ex.Message); }
    }
    void Demo()
    {
        var m = Monday(DateTime.Today).AddDays(-14);
        Func<int, int, int, string> dt = (day, h, mi) => m.AddDays(day).ToString("yyyyMMdd") + "T" + h.ToString("00") + mi.ToString("00") + "00";
        var E = new[] {
            new object[]{"Mathematics",0,8,30,10,0,"Room B204","MO,WE,FR"}, new object[]{"Physics",0,10,15,12,0,"Lab 3","MO,TH"},
            new object[]{"English Literature",1,8,30,10,0,"Room A110","TU,TH"}, new object[]{"Computer Science",1,13,0,15,0,"Lab 1","TU,FR"},
            new object[]{"History",2,10,15,11,45,"Room C012","WE"}, new object[]{"Lunch",0,12,0,13,0,"Cafeteria","MO,TU,WE,TH,FR"},
            new object[]{"Chemistry",2,13,30,15,30,"Lab 2","WE"}, new object[]{"Art & Design",3,14,0,16,0,"Studio","TH"},
            new object[]{"Sport",4,15,15,17,0,"Gym","FR"}, new object[]{"Study group",0,16,0,17,30,"Library","MO,WE"},
            new object[]{"Philosophy",4,10,15,11,45,"Room A207","FR"} };
        var sb = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nX-WR-CALNAME:Demo timetable\r\n");
        for (int i = 0; i < E.Length; i++)
        {
            var e = E[i];
            sb.Append("BEGIN:VEVENT\r\nUID:demo" + i + "\r\nSUMMARY:" + e[0] + "\r\nLOCATION:" + e[6] + "\r\nDTSTART:" + dt((int)e[1], (int)e[2], (int)e[3]) +
                      "\r\nDTEND:" + dt((int)e[1], (int)e[4], (int)e[5]) + "\r\nRRULE:FREQ=WEEKLY;BYDAY=" + e[7] + "\r\nEND:VEVENT\r\n");
        }
        sb.Append("END:VCALENDAR");
        AddText(sb.ToString(), "Demo timetable", sources.FirstOrDefault(s => s.Name == "Demo timetable"), "file", "");
        Changed();
    }

    /* ----- small helpers ----- */
    static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    static Color Mix(Color a, Color b, double t)
    {
        return Color.FromRgb((byte)(a.R * t + b.R * (1 - t)), (byte)(a.G * t + b.G * (1 - t)), (byte)(a.B * t + b.B * (1 - t)));
    }
    static Color Alpha(Color c, double a) { return Color.FromArgb((byte)(255 * a), c.R, c.G, c.B); }
    static DateTime Monday(DateTime d) { return d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7)); }
    static int IsoWeek(DateTime d) { var t = d.Date.AddDays(3 - ((int)d.DayOfWeek + 6) % 7); return (t.DayOfYear - 1) / 7 + 1; }
    static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }
    static string TimeOf(DateTime d) { return d.ToString("t"); }
    static string Range(Occ o) { return TimeOf(o.S) + " – " + TimeOf(o.E); }
    static bool SystemDark()
    {
        try { var v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1); return v is int && (int)v == 0; }
        catch { return false; }
    }
    TextBlock Txt(string s, double size, FontWeight w, Color c)
    {
        return new TextBlock { Text = s, FontSize = size, FontWeight = w, Foreground = B(c), TextTrimming = TextTrimming.CharacterEllipsis };
    }
    TextBlock Para(string s)
    {
        var t = Txt(s, 13, FontWeights.Normal, th.Mute); t.TextWrapping = TextWrapping.Wrap; t.LineHeight = 19; t.Margin = new Thickness(0, 6, 0, 0); return t;
    }
    TextBlock Head(string s)
    {
        var t = Txt(s.ToUpper(), 11, FontWeights.Bold, th.Mute); t.Margin = new Thickness(2, 0, 0, 8); return t;
    }
    // kind 0: normal, 1: primary, 2: icon, 3: small icon
    Border Btn(string text, Action click, int kind)
    {
        var t = Txt(text, kind >= 2 ? (kind == 3 ? 13 : 16) : 13, FontWeights.SemiBold, kind == 1 ? th.Bg : th.Ink);
        t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
        if (kind >= 2) t.Margin = new Thickness(0, -2, 0, 0);
        double h = kind == 3 ? 26 : 34;
        var b = new Border
        {
            Height = h, MinWidth = h, CornerRadius = new CornerRadius(kind == 3 ? 8 : 11), Background = B(kind == 1 ? th.Ink : th.Hover),
            Padding = new Thickness(kind >= 2 ? 0 : 13, 0, kind >= 2 ? 0 : 13, 0), Child = t, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center
        };
        b.MouseEnter += (s, e) => b.Opacity = .72; b.MouseLeave += (s, e) => b.Opacity = 1;
        b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
        return b;
    }
    void Toast(string msg)
    {
        toast.Child = Txt(msg, 13, FontWeights.SemiBold, th.Bg);
        toast.Background = B(th.Ink); toast.CornerRadius = new CornerRadius(13); toast.Padding = new Thickness(16, 10, 16, 10);
        toast.HorizontalAlignment = HorizontalAlignment.Center; toast.VerticalAlignment = VerticalAlignment.Bottom; toast.Margin = new Thickness(0, 0, 0, 26);
        toast.Visibility = Visibility.Visible; toastTimer.Stop(); toastTimer.Start();
    }
    void ApplyChrome()
    {
        try
        {
            var h = new WindowInteropHelper(this).Handle; if (h == IntPtr.Zero) return;
            int d = th.Dark ? 1 : 0; DwmSetWindowAttribute(h, 20, ref d, 4);
            int col = th.Bg.R | (th.Bg.G << 8) | (th.Bg.B << 16); DwmSetWindowAttribute(h, 35, ref col, 4);
        }
        catch { }
    }

    /* ----- input ----- */
    void Move(int n) { if (view == "exams") return; cursor = cursor.AddDays(n * (view == "day" ? 1 : 7)); Render(); }
    void OnKey(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && overlayKind != null) { CloseOverlay(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox) { if (e.Key == Key.Enter) { AddUrl(); e.Handled = true; } return; }
        if (overlayKind != null) return;
        if (e.Key == Key.Left) Move(-1);
        else if (e.Key == Key.Right) Move(1);
        else if (e.Key == Key.T) { cursor = DateTime.Today; Render(); }
        else if (e.Key == Key.D) SetView("day");
        else if (e.Key == Key.W) SetView("week");
        else if (e.Key == Key.E) SetView("exams");
    }

    /* ----- rendering ----- */
    List<DateTime> VisibleDays()
    {
        if (view == "day") return new List<DateTime> { cursor.Date };
        var m = Monday(cursor); return Enumerable.Range(0, days).Select(i => m.AddDays(i)).ToList();
    }
    static void Layout(List<Piece> items)
    {
        items.Sort((a, b) => a.A != b.A ? a.A.CompareTo(b.A) : b.B.CompareTo(a.B));
        var cluster = new List<Piece>(); var cols = new List<double>(); double end = -1;
        Action flush = () => { foreach (var it in cluster) it.N = cols.Count; cluster.Clear(); cols.Clear(); end = -1; };
        foreach (var it in items)
        {
            if (cluster.Count > 0 && it.A >= end) flush();
            int c = cols.FindIndex(x => x <= it.A);
            if (c < 0) { c = cols.Count; cols.Add(0); }
            cols[c] = it.VB; it.C = c; cluster.Add(it); end = Math.Max(end, it.VB);
        }
        flush();
    }
    UIElement Panel(UIElement child)
    {
        return new Border { Background = B(th.Panel), BorderBrush = B(th.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Child = child };
    }
    UIElement Blob(Color c, double x, double y)
    {
        var br = new RadialGradientBrush { Center = new Point(x, y), GradientOrigin = new Point(x, y), RadiusX = .5, RadiusY = .5 };
        br.GradientStops.Add(new GradientStop(c, 0)); br.GradientStops.Add(new GradientStop(Alpha(c, 0), 1));
        return new System.Windows.Shapes.Rectangle { Fill = br };
    }

    void Render()
    {
        double off = bodyScroll != null ? bodyScroll.VerticalOffset : 0;
        bool dark = theme == "dark" || (theme == "auto" && SystemDark());
        if (th == null || th.Dark != dark) { th = Theme.Make(dark); ApplyChrome(); }
        Background = B(th.Bg);
        bgLayer.Children.Clear();
        bgLayer.Children.Add(new System.Windows.Shapes.Rectangle { Fill = B(th.Bg) });
        bgLayer.Children.Add(Blob(th.B1, .12, .15)); bgLayer.Children.Add(Blob(th.B2, .9, .08)); bgLayer.Children.Add(Blob(th.B3, .62, .98));

        var g = new Grid { Margin = new Thickness(14) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(286) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var side = Panel(BuildSide()); var main = Panel(BuildMain());
        Grid.SetColumn(main, 2); g.Children.Add(side); g.Children.Add(main);
        content.Children.Clear(); content.Children.Add(g);
        if (off > 0 && bodyScroll != null) { var sv = bodyScroll; Dispatcher.BeginInvoke(new Action(() => sv.ScrollToVerticalOffset(off)), DispatcherPriority.Loaded); }
    }

    UIElement BuildMain()
    {
        var ds = VisibleDays(); DateTime from = ds[0], to = ds[ds.Count - 1].AddDays(1), now = DateTime.Now;
        var list = Occs(from, to);
        var ci = CultureInfo.CurrentCulture;
        bool exams = view == "exams"; string pill = ""; UIElement examBody = exams ? BuildExams(out pill) : null;

        // top bar
        var top = new Grid { Margin = new Thickness(20, 16, 20, 10) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mid = ds[Math.Min(3, ds.Count - 1)];
        var title = new TextBlock { FontSize = 27, VerticalAlignment = VerticalAlignment.Center };
        title.Inlines.Add(new System.Windows.Documents.Run(exams ? "Exams" : Cap(view == "day" ? ds[0].ToString("dddd d MMMM") : mid.ToString("MMMM")) + " ") { FontWeight = FontWeights.Bold, Foreground = B(th.Ink) });
        if (!exams) title.Inlines.Add(new System.Windows.Documents.Run(mid.Year.ToString()) { FontWeight = FontWeights.Light, Foreground = B(th.Mute) });
        top.Children.Add(title);
        var wk = new Border { Background = B(th.Hover), CornerRadius = new CornerRadius(12), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(12, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Txt(exams ? pill : "Week " + IsoWeek(ds[0]), 12, FontWeights.SemiBold, th.Mute) };
        if (exams && pill == "") wk.Visibility = Visibility.Collapsed;
        Grid.SetColumn(wk, 1); top.Children.Add(wk);
        var bar = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(bar, 3); top.Children.Add(bar);
        Action<UIElement> add = e => { ((FrameworkElement)e).Margin = new Thickness(8, 0, 0, 0); bar.Children.Add(e); };
        if (!exams) { add(Btn("‹", () => Move(-1), 2)); add(Btn("Today", () => { cursor = DateTime.Today; Render(); }, 0)); add(Btn("›", () => Move(1), 2)); }
        var seg = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var v in new[] { "day", "week", "exams" })
        {
            bool on = view == v; string vv = v;
            var sb = new Border { Height = 28, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 0, 12, 0), Background = B(on ? th.Solid : Colors.Transparent), Cursor = Cursors.Hand };
            var st = Txt(Cap(v), 13, FontWeights.SemiBold, on ? th.Ink : th.Mute); st.VerticalAlignment = VerticalAlignment.Center; sb.Child = st;
            sb.MouseLeftButtonUp += (s, e) => SetView(vv);
            seg.Children.Add(sb);
        }
        add(new Border { Background = B(th.Hover), CornerRadius = new CornerRadius(11), Padding = new Thickness(3), Child = seg, VerticalAlignment = VerticalAlignment.Center });
        if (view == "week") add(Btn(days == 7 ? "Hide weekends" : "Show weekends", () => { days = days == 7 ? 5 : 7; SaveState(); Render(); }, 0));
        add(Btn("◐", () => { theme = th.Dark ? "light" : "dark"; SaveState(); Render(); if (overlayKind == "src") ShowSources(); }, 2));
        add(Btn("Calendars", ShowSources, 1));

        if (exams)
        {
            var eg = new Grid();
            eg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); eg.RowDefinitions.Add(new RowDefinition());
            eg.Children.Add(top); Grid.SetRow((FrameworkElement)examBody, 1); eg.Children.Add(examBody);
            return eg;
        }

        // split into per-day pieces
        var timed = new List<List<Piece>>(); var longs = new List<List<Occ>>();
        double h0 = 8, h1 = 18;
        foreach (var d in ds)
        {
            DateTime a = d, b = d.AddDays(1); var tp = new List<Piece>(); var lg = new List<Occ>();
            foreach (var o in list)
            {
                if (o.S >= b || o.E <= a) continue;
                if (IsLong(o)) { lg.Add(o); continue; }
                double sm = ((o.S > a ? o.S : a) - a).TotalMinutes, em = Math.Max(((o.E < b ? o.E : b) - a).TotalMinutes, sm + 1);
                tp.Add(new Piece { O = o, A = sm, B = em, VB = Math.Max(em, sm + 28) });
                h0 = Math.Min(h0, Math.Floor(sm / 60)); h1 = Math.Max(h1, Math.Ceiling(em / 60));
            }
            Layout(tp); timed.Add(tp); longs.Add(lg);
        }
        h0 = Math.Max(0, h0); h1 = Math.Min(24, h1); int hrs = (int)(h1 - h0); double tot = hrs * 60;

        Func<Grid> cols = () =>
        {
            var gr = new Grid(); gr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            foreach (var d in ds) gr.ColumnDefinitions.Add(new ColumnDefinition());
            return gr;
        };

        // day headers
        var head = cols();
        for (int i = 0; i < ds.Count; i++)
        {
            bool today = ds[i] == now.Date;
            var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 10) };
            var wd = Txt(ds[i].ToString("ddd").TrimEnd('.').ToUpper(), 11.5, FontWeights.SemiBold, today ? th.Accent : th.Mute); wd.HorizontalAlignment = HorizontalAlignment.Center;
            var num = Txt(ds[i].Day.ToString(), 20, today ? FontWeights.SemiBold : FontWeights.Normal, today ? Colors.White : th.Ink);
            num.HorizontalAlignment = HorizontalAlignment.Center; num.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(wd);
            sp.Children.Add(new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = B(today ? th.Accent : Colors.Transparent), Margin = new Thickness(0, 4, 0, 0), Child = num, HorizontalAlignment = HorizontalAlignment.Center });
            Grid.SetColumn(sp, i + 1); head.Children.Add(sp);
        }

        // all-day row
        Grid allday = null;
        if (longs.Any(l => l.Count > 0))
        {
            allday = cols();
            var lab = Txt("all-day", 10, FontWeights.Normal, th.Mute); lab.HorizontalAlignment = HorizontalAlignment.Right; lab.Margin = new Thickness(0, 4, 9, 0); allday.Children.Add(lab);
            for (int i = 0; i < ds.Count; i++)
            {
                var sp = new StackPanel { Margin = new Thickness(3, 0, 3, 6) };
                foreach (var o in longs[i])
                {
                    Color c = ColorOf(o.M); var oo = o;
                    var chip = new Border { Background = B(Mix(c, th.Solid, th.EvMix)), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand, Child = Txt(o.M.Subject, 12, FontWeights.SemiBold, Mix(c, th.Ink, th.EvInk)) };
                    chip.MouseLeftButtonUp += (s, e) => ShowEvent(oo);
                    sp.Children.Add(chip);
                }
                Grid.SetColumn(sp, i + 1); allday.Children.Add(sp);
            }
        }

        // time grid
        var body = cols(); body.Height = hrs * 52;
        var hours = new FracPanel();
        bool ampm = ci.DateTimeFormat.ShortTimePattern.Contains("tt");
        for (int i = 1; i < hrs; i++)
        {
            var t = Txt(DateTime.Today.AddHours(h0 + i).ToString(ampm ? "h tt" : "HH:mm"), 11, FontWeights.Normal, th.Mute); t.TextAlignment = TextAlignment.Right;
            hours.Add(t, new Rect(0, (double)i / hrs, 1, 0), 1);
        }
        body.Children.Add(hours);
        for (int i = 0; i < ds.Count; i++)
        {
            bool today = ds[i] == now.Date;
            var col = new FracPanel { Hours = hrs, LineBrush = B(th.Line), LeftLine = true, Fill = today ? B(Alpha(th.Accent, .04)) : null };
            foreach (var p in timed[i])
            {
                double y0 = Math.Max(0, (p.A - h0 * 60) / tot), y1 = Math.Min(1, (p.B - h0 * 60) / tot);
                col.Add(EvBlock(p.O, p.B - p.A <= 35, p.O.E < now), new Rect((double)p.C / p.N, y0, 1.0 / p.N, y1 - y0), 0);
            }
            if (today)
            {
                double nm = now.TimeOfDay.TotalMinutes;
                if (nm >= h0 * 60 && nm <= h1 * 60)
                {
                    var nl = new Grid { IsHitTestVisible = false };
                    nl.Children.Add(new System.Windows.Shapes.Rectangle { Height = 2, Fill = B(th.Accent), VerticalAlignment = VerticalAlignment.Center });
                    nl.Children.Add(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = B(th.Accent), HorizontalAlignment = HorizontalAlignment.Left });
                    col.Add(nl, new Rect(0, (nm - h0 * 60) / tot, 1, 0), 2);
                }
            }
            Grid.SetColumn(col, i + 1); body.Children.Add(col);
        }
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body, Focusable = false, Margin = new Thickness(0, 0, 0, 12) };
        scroll.SizeChanged += (s, e) => body.Height = Math.Max(scroll.ActualHeight, hrs * 52);
        bodyScroll = scroll;

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition());
        main.Children.Add(top);
        Grid.SetRow(head, 1); main.Children.Add(head);
        if (allday != null) { Grid.SetRow(allday, 2); main.Children.Add(allday); }
        var sep = new Border { Height = 1, Background = B(th.Line) }; Grid.SetRow(sep, 3); main.Children.Add(sep);
        Grid.SetRow(scroll, 4); main.Children.Add(scroll);

        if (sources.Count == 0)
        {
            var sp = new StackPanel { Margin = new Thickness(30) };
            var h = Txt("Your timetable lives here", 22, FontWeights.Bold, th.Ink); h.HorizontalAlignment = HorizontalAlignment.Center;
            var p = Para("Add your Outlook calendar link or an .ics file to get started."); p.TextAlignment = TextAlignment.Center; p.FontSize = 14; p.Margin = new Thickness(0, 6, 0, 16);
            var b = Btn("Add a calendar", ShowSources, 1); b.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(h); sp.Children.Add(p); sp.Children.Add(b);
            var card = new Border { Background = B(th.Solid), BorderBrush = B(th.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Width = 380, Child = sp, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = .15, Direction = 270 } };
            Grid.SetRow(card, 4); main.Children.Add(card);
        }
        return main;
    }

    /* ----- exams tab: every subject with its next exam and the classes left before it ----- */
    static string InDays(int d) { return d <= 0 ? "today" : d == 1 ? "tomorrow" : "in " + d + " days"; }
    static string Hours(IEnumerable<Occ> l) { return l.Sum(o => (o.E - o.S).TotalHours).ToString("0.#") + " h"; }

    UIElement BuildExams(out string pill)
    {
        var now = DateTime.Now; pill = "";
        var subjects = new List<Subj>();
        foreach (var g in Occs(now.Date.AddDays(-300), now.Date.AddDays(500)).GroupBy(o => o.M.Key))
        {
            var s = new Subj { Name = g.First().M.Subject, C = ColorOf(g.First().M) };
            foreach (var o in g) (o.M.IsResit ? s.Resits : o.M.IsExam ? s.Exams : s.Sessions).Add(o);
            s.Next = s.Exams.FirstOrDefault(o => o.E > now);
            subjects.Add(s);
        }
        var withExam = subjects.Where(s => s.Next != null).OrderBy(s => s.Next.S).ToList();
        var rest = subjects.Where(s => s.Next == null && !Regex.IsMatch(s.Name, "^r[ée]servation", RegexOptions.IgnoreCase)
                                       && (s.Sessions.Any(o => o.E > now) || s.Resits.Any(o => o.E > now))
                                       && (s.Exams.Count + s.Resits.Count > 0 || s.Sessions.Count >= 2))
                           .OrderBy(s => s.Name).ToList();
        if (withExam.Count > 0) pill = "Next " + InDays((withExam[0].Next.S.Date - now.Date).Days);

        var page = new StackPanel { Margin = new Thickness(20, 4, 20, 20) };
        if (withExam.Count == 0 && rest.Count == 0)
        {
            var e1 = Txt(sources.Count == 0 ? "Add a calendar to see your exams" : "No upcoming exams found", 20, FontWeights.Bold, th.Ink); e1.Margin = new Thickness(0, 40, 0, 0); e1.HorizontalAlignment = HorizontalAlignment.Center;
            var e2 = Para("Exams are picked up automatically from your calendars: any event whose title or type says Exam, Examen, Partiel, DS, Contrôle, Midterm or Quiz."); e2.TextAlignment = TextAlignment.Center; e2.MaxWidth = 460;
            page.Children.Add(e1); page.Children.Add(e2);
            if (sources.Count == 0) { var b = Btn("Add a calendar", ShowSources, 1); b.HorizontalAlignment = HorizontalAlignment.Center; b.Margin = new Thickness(0, 16, 0, 0); page.Children.Add(b); }
        }
        if (withExam.Count > 0)
        {
            // two columns, each new card going to the shorter one
            var cols = new Grid(); cols.ColumnDefinitions.Add(new ColumnDefinition()); cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); cols.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel(); var right = new StackPanel(); Grid.SetColumn(right, 2); cols.Children.Add(left); cols.Children.Add(right);
            double wl = 0, wr = 0;
            foreach (var s in withExam)
            {
                double w = 5 + s.Sessions.Count(o => o.E > now && o.S < s.Next.S);
                if (wl <= wr) { left.Children.Add(ExamCard(s, now)); wl += w; } else { right.Children.Add(ExamCard(s, now)); wr += w; }
            }
            page.Children.Add(cols);
        }
        if (rest.Count > 0)
        {
            var h = Head("No upcoming exam"); h.Margin = new Thickness(4, 10, 0, 8); page.Children.Add(h);
            var list = new StackPanel();
            foreach (var s in rest)
            {
                var left = s.Sessions.Where(o => o.E > now).ToList();
                var lastExam = s.Exams.LastOrDefault(); var resit = s.Resits.FirstOrDefault(o => o.E > now);
                var g = new Grid { Margin = new Thickness(0, 7, 0, 7) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = B(s.C), HorizontalAlignment = HorizontalAlignment.Left });
                var n = Txt(s.Name, 13.5, FontWeights.SemiBold, th.Ink); Grid.SetColumn(n, 1); g.Children.Add(n);
                var info = Txt(Meta(left.Count > 0 ? left.Count + (left.Count == 1 ? " class left" : " classes left") + " · " + Hours(left) : "",
                                    left.Count > 0 ? "next " + left[0].S.ToString("ddd d MMM") : "",
                                    lastExam != null ? "exam was " + lastExam.S.ToString("d MMM") : "",
                                    resit != null ? "resit " + resit.S.ToString("d MMM") : ""), 12.5, FontWeights.Normal, th.Mute);
                info.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(info, 2); g.Children.Add(info);
                list.Children.Add(g);
            }
            page.Children.Add(new Border { Background = B(th.Solid), BorderBrush = B(th.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 8, 18, 8), Child = list });
        }
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = page, Focusable = false, Margin = new Thickness(0, 0, 0, 12) };
        bodyScroll = sv; return sv;
    }

    UIElement ExamCard(Subj s, DateTime now)
    {
        Color c = s.C, tint = Mix(c, th.Ink, th.EvInk); var ex = s.Next;
        var before = s.Sessions.Where(o => o.E > now && o.S < ex.S).ToList();
        int d = (ex.S.Date - now.Date).Days;
        var sp = new StackPanel();

        // header: subject, exam date and a countdown
        var hd = new Grid { Cursor = Cursors.Hand, Background = Brushes.Transparent };
        hd.ColumnDefinitions.Add(new ColumnDefinition()); hd.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hs = new StackPanel();
        var name = Txt(s.Name, 16.5, FontWeights.Bold, th.Ink); name.TextWrapping = TextWrapping.Wrap; hs.Children.Add(name);
        var when = Txt(Cap(ex.S.ToString("dddd d MMMM")) + " · " + Range(ex), 13, FontWeights.SemiBold, tint); when.Margin = new Thickness(0, 4, 0, 0); when.TextWrapping = TextWrapping.Wrap; hs.Children.Add(when);
        if (ex.M.Loc != "") { var rm = Txt(Room(ex.M), 12, FontWeights.Normal, th.Mute); rm.Margin = new Thickness(0, 2, 0, 0); hs.Children.Add(rm); }
        hd.Children.Add(hs);
        var cd = new StackPanel { Margin = new Thickness(12, -4, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var big = Txt(d <= 0 ? "Today" : d == 1 ? "Tomorrow" : d.ToString(), d <= 1 ? 18 : 32, FontWeights.Bold, c); big.HorizontalAlignment = HorizontalAlignment.Right; cd.Children.Add(big);
        if (d > 1) { var dl = Txt("days", 11, FontWeights.SemiBold, th.Mute); dl.HorizontalAlignment = HorizontalAlignment.Right; dl.Margin = new Thickness(0, -5, 0, 0); cd.Children.Add(dl); }
        Grid.SetColumn(cd, 1); hd.Children.Add(cd);
        hd.MouseLeftButtonUp += (a, e) => ShowEvent(ex);
        sp.Children.Add(hd);
        sp.Children.Add(new Border { Height = 1, Background = B(th.Line), Margin = new Thickness(0, 12, 0, 10) });

        // classes left before the exam
        var kinds = string.Join(", ", before.Where(o => o.M.Kind != "").GroupBy(o => o.M.Kind).Select(g => g.Count() + " " + g.Key));
        var bh = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        bh.Children.Add(Txt("BEFORE THE EXAM", 10.5, FontWeights.Bold, th.Mute));
        var tot = Txt(before.Count == 0 ? "" : Meta(before.Count + (before.Count == 1 ? " class" : " classes"), Hours(before), kinds), 11.5, FontWeights.SemiBold, th.Mute);
        tot.HorizontalAlignment = HorizontalAlignment.Right; tot.Margin = new Thickness(120, 0, 0, 0); bh.Children.Add(tot);
        sp.Children.Add(bh);
        if (before.Count == 0) sp.Children.Add(Txt("No more classes before this exam.", 12.5, FontWeights.Normal, th.Mute));
        foreach (var o in before)
        {
            var oo = o; bool today = o.S.Date == now.Date;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Txt(today ? "Today" : Cap(o.S.ToString("ddd d MMM")), 12.5, FontWeights.SemiBold, today ? th.Accent : th.Ink));
            var tm = Txt(Range(o), 12.5, FontWeights.Normal, th.Ink); Grid.SetColumn(tm, 1); g.Children.Add(tm);
            if (o.M.Kind != "")
            {
                var kp = new Border { Background = B(Alpha(c, .16)), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2), HorizontalAlignment = HorizontalAlignment.Left, Child = Txt(o.M.Kind, 10.5, FontWeights.Bold, tint) };
                Grid.SetColumn(kp, 2); g.Children.Add(kp);
            }
            var rm = Txt(Room(o.M), 12, FontWeights.Normal, th.Mute); Grid.SetColumn(rm, 3); g.Children.Add(rm);
            var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(-8, 0, -8, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = g };
            row.MouseEnter += (a, e) => row.Background = B(th.Hover); row.MouseLeave += (a, e) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (a, e) => ShowEvent(oo);
            sp.Children.Add(row);
        }

        // what comes after
        var later = s.Exams.Where(o => o.S > ex.S).Select(o => "exam " + o.S.ToString("d MMM")).Concat(s.Resits.Where(o => o.E > now).Select(o => "resit " + o.S.ToString("d MMM"))).ToList();
        if (later.Count > 0) { var lt = Txt("Then: " + string.Join(" · ", later), 12, FontWeights.Normal, th.Mute); lt.TextWrapping = TextWrapping.Wrap; lt.Margin = new Thickness(0, 10, 0, 0); sp.Children.Add(lt); }

        var body = new Grid();
        body.Children.Add(new Border { Width = 5, Background = B(c), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(17, 0, 0, 17) });
        sp.Margin = new Thickness(22, 16, 18, 16); body.Children.Add(sp);
        return new Border { Background = B(th.Solid), BorderBrush = B(th.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 0, 14), Child = body };
    }

    UIElement EvBlock(Occ o, bool tiny, bool past)
    {
        bool exam = o.M.IsExam; // exams are drawn as solid blocks so they stand out
        Color c = ColorOf(o.M), fg = exam ? Colors.White : Mix(c, th.Ink, th.EvInk), bg = exam ? Mix(c, th.Dark ? Colors.Black : th.Ink, th.Dark ? .8 : .88) : Mix(c, th.Solid, th.EvMix);
        if (past) { bg = Mix(bg, th.Solid, .5); fg = exam ? Mix(fg, bg, .8) : Mix(fg, th.Solid, .6); }
        var g = new Grid();
        g.Children.Add(new Border { Width = 3.5, CornerRadius = new CornerRadius(2), Background = B(exam ? Alpha(Colors.White, .75) : past ? Mix(c, th.Solid, .55) : c), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 4, 0, 4) });
        var sp = new StackPanel { Margin = new Thickness(11, tiny ? 1 : 5, 7, 0) };
        var t = Txt(o.M.Subject, 12.5, FontWeights.SemiBold, fg);
        if (!tiny) { t.TextWrapping = TextWrapping.Wrap; t.LineHeight = 15; t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; t.MaxHeight = 45; }
        sp.Children.Add(t);
        if (!tiny) { var m = Txt(Meta(exam ? KindLabel(o.M).ToUpper() : o.M.Kind, TimeOf(o.S), Room(o.M)), 11, exam ? FontWeights.SemiBold : FontWeights.Normal, Mix(fg, bg, .8)); m.Margin = new Thickness(0, 2, 0, 0); sp.Children.Add(m); }
        g.Children.Add(sp);
        var bd = new Border { CornerRadius = new CornerRadius(9), Background = B(bg), Margin = new Thickness(2, 1, 2, 1), Cursor = Cursors.Hand, ClipToBounds = true, Child = g };
        var hover = B(exam ? Mix(bg, Colors.White, .88) : Mix(c, th.Solid, th.EvMix + .12)); var normal = bd.Background;
        bd.MouseEnter += (s, e) => bd.Background = hover; bd.MouseLeave += (s, e) => bd.Background = normal;
        bd.MouseLeftButtonUp += (s, e) => ShowEvent(o);
        return bd;
    }

    UIElement BuildSide()
    {
        var now = DateTime.Now; var sp = new StackPanel { Margin = new Thickness(18, 20, 18, 18) };
        var clock = Txt(TimeOf(now), 50, FontWeights.SemiBold, th.Ink); clock.Margin = new Thickness(0, -8, 0, 0);
        sp.Children.Add(clock);
        var date = new TextBlock { FontSize = 14, Margin = new Thickness(1, 2, 0, 22), TextWrapping = TextWrapping.Wrap };
        date.Inlines.Add(new System.Windows.Documents.Run(Cap(now.ToString("dddd")) + " ") { Foreground = B(th.Accent), FontWeight = FontWeights.SemiBold });
        date.Inlines.Add(new System.Windows.Documents.Run(now.ToString("d MMMM yyyy")) { Foreground = B(th.Mute) });
        sp.Children.Add(date);

        // mini month
        var first = new DateTime(cursor.Year, cursor.Month, 1); var g0 = Monday(first); var vis = VisibleDays();
        var mt = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        var ml = Txt(Cap(first.ToString("MMMM yyyy")), 14, FontWeights.SemiBold, th.Ink); ml.VerticalAlignment = VerticalAlignment.Center; mt.Children.Add(ml);
        var mb = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        mb.Children.Add(Btn("‹", () => { cursor = first.AddMonths(-1); Render(); }, 3));
        var nx = Btn("›", () => { cursor = first.AddMonths(1); Render(); }, 3); nx.Margin = new Thickness(4, 0, 0, 0); mb.Children.Add(nx);
        mt.Children.Add(mb); sp.Children.Add(mt);
        var busy = new HashSet<DateTime>(Occs(g0, g0.AddDays(42)).Select(o => (o.S < g0 ? g0 : o.S).Date));
        var mini = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 22) };
        for (int i = 0; i < 7; i++)
        {
            var t = Txt(g0.AddDays(i).ToString("ddd").Substring(0, 1).ToUpper(), 10, FontWeights.SemiBold, th.Mute); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 4, 0, 4);
            mini.Children.Add(t);
        }
        for (int i = 0; i < 42; i++)
        {
            var d = g0.AddDays(i); bool inV = vis.Contains(d), today = d == now.Date, outM = d.Month != first.Month;
            Color fg = today ? Colors.White : outM ? Alpha(th.Mute, .5) : th.Ink;
            var cell = new Grid { Height = 28, Cursor = Cursors.Hand, Background = Brushes.Transparent, Margin = new Thickness(0, 1, 0, 1) };
            if (inV) cell.Children.Add(new Border { Background = B(th.Hover), CornerRadius = new CornerRadius(d == vis[0] ? 9 : 0, d == vis[vis.Count - 1] ? 9 : 0, d == vis[vis.Count - 1] ? 9 : 0, d == vis[0] ? 9 : 0) });
            if (today) cell.Children.Add(new Border { Background = B(th.Accent), CornerRadius = new CornerRadius(9) });
            var t = Txt(d.Day.ToString(), 12, today ? FontWeights.Bold : FontWeights.Normal, fg); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, -3, 0, 0);
            cell.Children.Add(t);
            if (busy.Contains(d)) cell.Children.Add(new System.Windows.Shapes.Ellipse { Width = 3, Height = 3, Fill = B(Alpha(fg, .6)), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) });
            cell.MouseLeftButtonUp += (s, e) => { cursor = d; Render(); };
            mini.Children.Add(cell);
        }
        sp.Children.Add(mini);

        // up next
        sp.Children.Add(Head("Up next"));
        var up = Occs(now.Date, now.Date.AddDays(15)).Where(o => o.E > now && !IsLong(o)).Take(6).ToList();
        string last = "";
        foreach (var o in up)
        {
            int diff = (o.S.Date - now.Date).Days;
            string lab = diff <= 0 ? "Today" : diff == 1 ? "Tomorrow" : Cap(o.S.ToString("dddd d MMM"));
            if (lab != last) { var l = Txt(lab, 12, FontWeights.SemiBold, th.Mute); l.Margin = new Thickness(2, last == "" ? 0 : 8, 0, 6); sp.Children.Add(l); last = lab; }
            Color c = ColorOf(o.M); bool live = o.S <= now; var oo = o;
            var row = new Grid();
            row.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = B(c), HorizontalAlignment = HorizontalAlignment.Left });
            var st = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            if (live) st.Children.Add(Txt("NOW", 10, FontWeights.Bold, c));
            else if (o.M.IsExam) st.Children.Add(Txt(KindLabel(o.M).ToUpper(), 10, FontWeights.Bold, c));
            var tt = Txt(o.M.Subject, 13.5, FontWeights.SemiBold, th.Ink); tt.TextWrapping = TextWrapping.Wrap; st.Children.Add(tt);
            var mm = Txt(Meta(Range(o), o.M.IsExam ? "" : o.M.Kind, Room(o.M)), 12, FontWeights.Normal, th.Mute); mm.Margin = new Thickness(0, 2, 0, 0); st.Children.Add(mm);
            if (live)
            {
                double p = Math.Max(0.01, Math.Min(1, (now - o.S).TotalSeconds / Math.Max(1, (o.E - o.S).TotalSeconds)));
                var pg = new Grid { Height = 3, Margin = new Thickness(0, 7, 0, 0) };
                pg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(p, GridUnitType.Star) }); pg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - p, GridUnitType.Star) });
                pg.Children.Add(new Border { Background = B(Alpha(c, .25)), CornerRadius = new CornerRadius(2) }); Grid.SetColumnSpan(pg.Children[0], 2);
                pg.Children.Add(new Border { Background = B(c), CornerRadius = new CornerRadius(2) });
                st.Children.Add(pg);
            }
            row.Children.Add(st);
            var card = new Border { Background = B(Alpha(c, .13)), CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Child = row };
            card.MouseEnter += (s, e) => card.Background = B(Alpha(c, .22)); card.MouseLeave += (s, e) => card.Background = B(Alpha(c, .13));
            card.MouseLeftButtonUp += (s, e) => ShowEvent(oo);
            sp.Children.Add(card);
        }
        if (up.Count == 0) sp.Children.Add(Txt("Nothing coming up. Enjoy the calm.", 13, FontWeights.Normal, th.Mute));
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = sp, Focusable = false };
    }

    /* ----- overlays ----- */
    void CloseOverlay() { if (urlBox != null) urlText = urlBox.Text; urlBox = null; overlay.Children.Clear(); overlayKind = null; }
    void ShowOverlay(string kind, UIElement inner)
    {
        overlay.Children.Clear(); overlayKind = kind;
        var back = new Border { Background = B(Color.FromArgb(th.Dark ? (byte)150 : (byte)90, 10, 10, 20)) };
        back.MouseLeftButtonUp += (s, e) => CloseOverlay();
        overlay.Children.Add(back);
        var card = new Border
        {
            Background = B(th.Solid), BorderBrush = B(th.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Width = 480,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 30, 0, 30),
            Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 14, Opacity = .35, Direction = 270 },
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = inner, Focusable = false }
        };
        overlay.Children.Add(card);
    }
    void ShowEvent(Occ o)
    {
        var m = o.M; Color c = ColorOf(m);
        var outer = new StackPanel();
        outer.Children.Add(new Border { Height = 8, Background = B(c), CornerRadius = new CornerRadius(21, 21, 0, 0) });
        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 22) }; outer.Children.Add(sp);
        var t = Txt(m.Subject, 21, FontWeights.Bold, th.Ink); t.TextWrapping = TextWrapping.Wrap; sp.Children.Add(t);
        string sub = Meta(KindLabel(m), m.Teacher, m.Code);
        if (sub != "") { var st = Txt(sub, 13, FontWeights.SemiBold, Mix(c, th.Ink, th.EvInk)); st.TextWrapping = TextWrapping.Wrap; st.Margin = new Thickness(0, 4, 0, 0); sp.Children.Add(st); }
        string when;
        if (m.AllDay)
        {
            var le = o.E.AddDays(-1);
            when = (o.S.Date == le.Date ? Cap(o.S.ToString("dddd d MMMM")) : o.S.ToString("d MMM") + " – " + le.ToString("d MMM")) + " · all day";
        }
        else when = Cap(o.S.ToString("dddd d MMMM")) + "\n" + Range(o);
        Action<string, string> row = (icon, text) =>
        {
            var g = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = icon, FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe Fluent Icons"), FontSize = 15, Foreground = B(th.Mute), Margin = new Thickness(0, 3, 0, 0) });
            var v = Txt(text, 14, FontWeights.Normal, th.Ink); v.TextWrapping = TextWrapping.Wrap; v.LineHeight = 20; Grid.SetColumn(v, 1); g.Children.Add(v);
            sp.Children.Add(g);
        };
        row("", when);
        if (m.Loc != "") row("", Room(m));
        if (m.R != null) row("", "Repeats");
        string desc = Regex.Replace(m.Desc, @"\s*§\s*", "\n");
        if (desc != "") row("", desc.Length > 1500 ? desc.Substring(0, 1500) + "…" : desc);
        var close = Btn("Close", CloseOverlay, 1); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 22, 0, 0);
        sp.Children.Add(close);
        ShowOverlay("event", outer);
    }
    static string Ago(DateTime t)
    {
        double m = (DateTime.Now - t).TotalMinutes;
        return m < 1 ? "just now" : m < 60 ? (int)m + " min ago" : m < 1440 ? (int)(m / 60) + " h ago" : (int)(m / 1440) + " d ago";
    }
    void ShowSources()
    {
        if (urlBox != null) urlText = urlBox.Text;
        var sp = new StackPanel { Margin = new Thickness(24) };
        sp.Children.Add(Txt("Calendars", 21, FontWeights.Bold, th.Ink));
        sp.Children.Add(Para("Add a calendar file (.ics) or paste a calendar link to keep it in sync."));
        foreach (var src in sources.ToList())
        {
            var s = src;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var st = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(Txt(s.Name, 14, FontWeights.SemiBold, th.Ink));
            st.Children.Add(Txt(s.Error != "" ? s.Error : s.Masters.Count + " events · " + (s.Type == "url" ? "synced " : "added ") + Ago(s.Updated), 12, FontWeights.Normal, s.Error != "" ? th.Accent : th.Mute));
            g.Children.Add(st);
            var r = Btn("↻", () =>
            {
                if (s.Type == "url") { var t = SyncSource(s, false); return; }
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Choose the newer file for " + s.Name, Filter = "Calendar files (*.ics)|*.ics;*.ical|All files|*.*" };
                if (dlg.ShowDialog(this) != true) return;
                try { int n = AddText(File.ReadAllText(dlg.FileName, Encoding.UTF8), s.Name, s, "file", Path.GetFileNameWithoutExtension(dlg.FileName)); Toast("Calendar updated · " + n + " events"); }
                catch (Exception ex) { Toast(ex.Message); }
                Changed();
            }, 2);
            r.ToolTip = s.Type == "url" ? "Refresh now" : "Replace with a newer file";
            r.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(r, 1); g.Children.Add(r);
            var x = Btn("✕", () => { sources.Remove(s); try { File.Delete(Path.Combine(dataDir, s.Id + ".ics")); } catch { } Changed(); }, 2);
            x.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(x, 2); g.Children.Add(x);
            sp.Children.Add(new Border { Background = B(th.Hover), CornerRadius = new CornerRadius(13), Padding = new Thickness(12, 9, 9, 9), Margin = new Thickness(0, 8, 0, 0), Child = g });
        }
        var h1 = Head("From a file"); h1.Margin = new Thickness(2, 22, 0, 8); sp.Children.Add(h1);
        var fr = new StackPanel { Orientation = Orientation.Horizontal };
        fr.Children.Add(Btn("Choose .ics file…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Calendar files (*.ics)|*.ics;*.ical|All files|*.*", Multiselect = true };
            if (dlg.ShowDialog(this) == true) AddFiles(dlg.FileNames);
        }, 0));
        var hint = Txt("or drop it anywhere on the window", 12, FontWeights.Normal, th.Mute); hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(10, 0, 0, 0); fr.Children.Add(hint);
        sp.Children.Add(fr);
        var h2 = Head("From Outlook / a link"); h2.Margin = new Thickness(2, 22, 0, 8); sp.Children.Add(h2);
        var ur = new Grid(); ur.ColumnDefinitions.Add(new ColumnDefinition()); ur.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        urlBox = new TextBox { Text = urlText, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = B(th.Ink), CaretBrush = B(th.Ink), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 8, 0) };
        var ph = Txt("Paste the ICS link here (https://…)", 13, FontWeights.Normal, th.Mute); ph.IsHitTestVisible = false; ph.VerticalAlignment = VerticalAlignment.Center; ph.Margin = new Thickness(12, 0, 0, 0);
        ph.Visibility = urlText == "" ? Visibility.Visible : Visibility.Collapsed;
        urlBox.TextChanged += (s, e) => ph.Visibility = urlBox.Text == "" ? Visibility.Visible : Visibility.Collapsed;
        var ug = new Grid(); ug.Children.Add(urlBox); ug.Children.Add(ph);
        ur.Children.Add(new Border { Height = 38, CornerRadius = new CornerRadius(11), Background = B(th.Hover), BorderBrush = B(th.Line), BorderThickness = new Thickness(1), Child = ug });
        var ab = Btn("Add", AddUrl, 1); ab.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(ab, 1); ur.Children.Add(ab);
        sp.Children.Add(ur);
        sp.Children.Add(Para("Outlook: open Outlook on the web → Settings → Calendar → Shared calendars → Publish a calendar → choose \"Can view all details\" → Publish, then copy the ICS link and paste it here. It refreshes automatically every 15 minutes."));
        var foot = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        var db = Btn("Load demo timetable", () => { Demo(); Toast("Demo timetable loaded"); }, 0); db.HorizontalAlignment = HorizontalAlignment.Left; foot.Children.Add(db);
        var done = Btn("Done", CloseOverlay, 1); done.HorizontalAlignment = HorizontalAlignment.Right; foot.Children.Add(done);
        sp.Children.Add(foot);
        ShowOverlay("src", sp);
    }
}

static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);

    [STAThread]
    static void Main(string[] args)
    {
        // --background: no window; just keeps the calendars (and the live wallpaper) up to date
        bool testing = args.Contains("--shot"), background = args.Contains("--background"), created = true;
        Mutex mutex = testing ? null : new Mutex(true, background ? "Timetable.Background" : "Timetable.SingleInstance", out created);
        if (!created)
        {
            if (background) return;
            var h = FindWindow(null, "Timetable");
            if (h != IntPtr.Zero) { if (IsIconic(h)) ShowWindow(h, 9); SetForegroundWindow(h); }
            return;
        }
        if (background)
        {
            var bg = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            bg.DispatcherUnhandledException += (s, e) => e.Handled = true;
            var hidden = new MainWindow(args);
            bg.Run();
            GC.KeepAlive(hidden); GC.KeepAlive(mutex);
            return;
        }
        var app = new Application();
        app.DispatcherUnhandledException += (s, e) =>
        {
            e.Handled = true;
            if (!testing) MessageBox.Show(e.Exception.Message, "Timetable", MessageBoxButton.OK, MessageBoxImage.Warning);
            else { Console.Error.WriteLine(e.Exception); Environment.Exit(1); }
        };
        app.Run(new MainWindow(args));
        GC.KeepAlive(mutex);
    }
}
