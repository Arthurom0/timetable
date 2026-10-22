#!/bin/bash
# Builds Timetable-Mac.dmg, which holds:
#   Timetable.app          ready to use: drag it onto the Applications shortcut
#   Build it yourself/     "Build Timetable" (double-click to rebuild from source, no Terminal) and the Source
set -e
cd "$(dirname "$0")"
CI="${CI:-1}" bash build.sh

SOURCE=(main.swift Timetable.html Timetable.icns build.sh)
BUILDER="dmg/Build it yourself/Build Timetable.app"

rm -rf dmg Timetable-Mac.dmg
mkdir -p "dmg/Build it yourself/Source"
cp -R Timetable.app dmg/
ln -s /Applications dmg/Applications
cp "${SOURCE[@]}" "dmg/Build it yourself/Source/"

osacompile -o "$BUILDER" "builder/Build Timetable.applescript"
mkdir -p "$BUILDER/Contents/Resources/Source"
cp "${SOURCE[@]}" "$BUILDER/Contents/Resources/Source/"
cp Timetable.icns "$BUILDER/Contents/Resources/applet.icns"
# osacompile's Assets.car would win over applet.icns, so drop it to show the Timetable icon
rm -f "$BUILDER/Contents/Resources/Assets.car"
plutil -remove CFBundleIconName "$BUILDER/Contents/Info.plist" 2>/dev/null || true
xattr -cr "$BUILDER"
codesign --force --sign - "$BUILDER"

hdiutil create -volname Timetable -srcfolder dmg -fs HFS+ -format UDZO -ov Timetable-Mac.dmg
rm -rf dmg
echo "Done: $(pwd)/Timetable-Mac.dmg"
