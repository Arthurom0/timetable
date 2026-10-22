-- "Build Timetable": builds Timetable.app from source and installs it, with no Terminal.
-- It uses the Source folder next to it if there is one (so edits there are picked up),
-- otherwise the copy of the source inside this app. make-dmg.sh compiles it with osacompile.

set myPath to POSIX path of (path to me)
set nextSrc to (do shell script "dirname " & quoted form of myPath) & "/Source"
set bundledSrc to myPath & "Contents/Resources/Source"
if (do shell script "test -f " & quoted form of (nextSrc & "/build.sh") & " && echo yes || echo no") is "yes" then
	set src to nextSrc
else
	set src to bundledSrc
end if

-- swiftc comes with Apple's free command line tools; asking for them opens Apple's installer window
try
	do shell script "xcode-select -p"
on error
	do shell script "xcode-select --install >/dev/null 2>&1 &"
	display dialog "Building Timetable needs Apple's free developer tools." & return & return & "Click Install in the window that just opened. When it has finished, open Build Timetable again." buttons {"OK"} default button 1 with title "Build Timetable" with icon caution
	return
end try

display notification "This takes about a minute." with title "Building Timetable…"
try
	-- the disk image is read-only, so build in a temporary folder
	set work to do shell script "mktemp -d"
	do shell script "cp -R " & quoted form of src & " " & quoted form of (work & "/src") & " && cd " & quoted form of (work & "/src") & " && CI=1 bash build.sh 2>&1"
on error msg
	display dialog "Timetable could not be built:" & return & return & msg buttons {"OK"} default button 1 with title "Build Timetable" with icon stop
	return
end try

set built to work & "/src/Timetable.app"
try
	do shell script "rm -rf /Applications/Timetable.app && ditto " & quoted form of built & " /Applications/Timetable.app"
	set dest to "/Applications/Timetable.app"
on error
	do shell script "mkdir -p ~/Applications && rm -rf ~/Applications/Timetable.app && ditto " & quoted form of built & " ~/Applications/Timetable.app"
	set dest to (POSIX path of (path to home folder)) & "Applications/Timetable.app"
end try
do shell script "rm -rf " & quoted form of work

set answer to button returned of (display dialog "Timetable is built and installed in " & dest & "." buttons {"Show in Finder", "Open Timetable"} default button 2 with title "Build Timetable")
if answer is "Open Timetable" then
	do shell script "open " & quoted form of dest
else
	do shell script "open -R " & quoted form of dest
end if
