# Timetable

A good-looking timetable for your Outlook (or any `.ics`) calendar, with an **Exams** tab that lists every subject, its next exam and every class left before it.

## Windows app — `Timetable/`

Double-click `Timetable/Timetable.exe`. Nothing to install.

- Click **Calendars** to add your Outlook ICS link (it refreshes every 15 minutes) or an `.ics` file. Dropping a newer file with the same name replaces the old one.
- **Day / Week / Exams** views, light and dark mode. Keys: ← → (previous / next), T (today), D, W, E (views).
- Exams are found automatically from words such as *Exam, Examen, Partiel, DS, Contrôle, Midterm, Quiz*; resits from *Rattrapage, Resit*.
- School-style titles like `[CODE]Subject [TD]Teacher` are split into subject, type and teacher.
- `Timetable.exe --background` runs with no window and only keeps calendars (and the wallpaper) up to date.

Your calendars are stored in `%AppData%\Timetable`, not in this folder.

To rebuild the app after changing `Timetable/source/Timetable.cs`, run `Timetable/source/build.ps1` (it uses the C# compiler built into Windows).

## Live wallpaper — `Timetable/wallpaper/`

A version of the timetable for [Lively Wallpaper](https://apps.microsoft.com/detail/9ntm2qc6qws7). Zip the contents of `Timetable/wallpaper/` and add the zip in Lively. The Windows app writes your calendar into the wallpaper (`data.js`) each time it syncs.

## Mac app — `Timetable-Mac/`

Built on the Mac itself: in Terminal, run `bash build.sh` from that folder (needs Apple's command line tools; it offers to install them). This version has no Exams tab yet.
