#!/bin/bash
# Builds Timetable.app and wraps it in Timetable-Mac.dmg (drag the app onto the Applications shortcut).
set -e
cd "$(dirname "$0")"
CI="${CI:-1}" bash build.sh

rm -rf dmg Timetable-Mac.dmg
mkdir dmg
cp -R Timetable.app dmg/
ln -s /Applications dmg/Applications
hdiutil create -volname Timetable -srcfolder dmg -fs HFS+ -format UDZO -ov Timetable-Mac.dmg
rm -rf dmg
echo "Done: $(pwd)/Timetable-Mac.dmg"
