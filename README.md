# Timetable

A good-looking timetable for your Outlook (or any `.ics`) calendar, with an **Exams** tab that lists every subject, its next exam and every class left before it.

## Download

| | |
|---|---|
| **Mac** (macOS 13 or later, Apple silicon and Intel) | [Timetable-Mac.dmg](https://github.com/Arthurom0/timetable/releases/latest/download/Timetable-Mac.dmg) |
| **Windows** (10 and 11) | [Timetable-Windows.zip](https://github.com/Arthurom0/timetable/releases/latest/download/Timetable-Windows.zip) (app + live wallpaper) or just [Timetable.exe](https://github.com/Arthurom0/timetable/releases/latest/download/Timetable.exe) |

Older versions are on the [Releases page](https://github.com/Arthurom0/timetable/releases).

**Mac:** open the `.dmg` and drag **Timetable** onto **Applications**. (The `.dmg` also has a **Build it yourself** folder: double-click **Build Timetable** to rebuild the app from the `Source` folder next to it and install it, no Terminal needed. Copy the folder somewhere first if you want to edit the source.) The app isn't signed by a registered Apple developer, so the first time you open it macOS says it "cannot be opened because Apple cannot check it for malicious software". Click **Done**, then go to **System Settings → Privacy & Security**, scroll down to the message about Timetable and click **Open Anyway** (enter your password if asked). After that it opens normally.

**Windows:** unzip `Timetable-Windows.zip` (right-click → **Extract All**) and double-click `Timetable.exe`. To rebuild it from the included `source` folder, double-click **Build Timetable.bat**. Because the app isn't signed, Windows SmartScreen may show "Windows protected your PC": click **More info**, then **Run anyway**. Your browser may also ask whether to keep the download; choose **Keep**.

## Windows app — `Timetable/`

Double-click `Timetable/Timetable.exe` (or the downloaded one). Nothing to install.

- Click **Calendars** to add your Outlook ICS link (it refreshes every 15 minutes) or an `.ics` file. Dropping a newer file with the same name replaces the old one.
- **Day / Week / Exams** views, light and dark mode. Keys: ← → (previous / next), T (today), D, W, E (views).
- Exams are found automatically from words such as *Exam, Examen, Partiel, DS, Contrôle, Midterm, Quiz*; resits from *Rattrapage, Resit*.
- School-style titles like `[CODE]Subject [TD]Teacher` are split into subject, type and teacher.
- `Timetable.exe --background` runs with no window and only keeps calendars (and the wallpaper) up to date.

Your calendars are stored in `%AppData%\Timetable`, not in this folder.

To rebuild the app after changing `Timetable/source/Timetable.cs`, double-click `Timetable/Build Timetable.bat` (it runs `source/build.ps1`, which uses the C# compiler built into Windows).

## Live wallpaper — `Timetable/wallpaper/`

A version of the timetable for [Lively Wallpaper](https://apps.microsoft.com/detail/9ntm2qc6qws7). Zip the contents of `Timetable/wallpaper/` and add the zip in Lively. The Windows app writes your calendar into the wallpaper (`data.js`) each time it syncs.

## Mac app — `Timetable-Mac/`

Same features as the Windows app, in a native Mac window. Your calendars are stored in `~/Library/Application Support/Timetable`.

To build it yourself instead of downloading it, either double-click **Build Timetable** in the downloaded `.dmg`, or in Terminal run `bash build.sh` from that folder (needs Apple's command line tools; it offers to install them). `bash make-dmg.sh` builds the app and wraps it in `Timetable-Mac.dmg`.

## Publishing a new version

The workflow in `.github/workflows/release.yml` builds both apps on GitHub's Mac and Windows machines. To publish a release:

```bash
git tag v1.0.0
git push origin v1.0.0
```

A few minutes later the release appears on the Releases page with `Timetable-Mac.dmg`, `Timetable-Windows.zip` and `Timetable.exe` attached, and the download links above point to it. Use a higher number (`v1.0.1`, `v1.1.0`, …) for each new version. To try a build without releasing, open the **Actions** tab, choose **Release** and click **Run workflow**; the files are on that run's page.
