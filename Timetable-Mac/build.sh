#!/bin/bash
# Builds Timetable.app on the Mac itself, as one app that runs natively on Apple silicon and Intel Macs.
# Needs Apple's free command line tools; if they are missing, macOS offers to install them.
set -e
cd "$(dirname "$0")"

if ! xcode-select -p >/dev/null 2>&1; then
  echo "Apple's command line tools are needed. A window will offer to install them."
  echo "When that has finished, run this script again."
  xcode-select --install
  exit 1
fi

APP="Timetable.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

echo "Compiling..."
for ARCH in arm64 x86_64; do
  swiftc -O main.swift -target $ARCH-apple-macos13 -o "Timetable-$ARCH" -framework Cocoa -framework WebKit
done
lipo -create Timetable-arm64 Timetable-x86_64 -output "$APP/Contents/MacOS/Timetable"
rm Timetable-arm64 Timetable-x86_64

cp Timetable.html Timetable.icns "$APP/Contents/Resources/"

# VERSION is set by the release workflow from the git tag (v1.2.0 -> 1.2.0)
VERSION="${VERSION:-1.0}"
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Timetable</string>
  <key>CFBundleDisplayName</key><string>Timetable</string>
  <key>CFBundleIdentifier</key><string>local.timetable.app</string>
  <key>CFBundleExecutable</key><string>Timetable</string>
  <key>CFBundleIconFile</key><string>Timetable</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

# Files on the Desktop pick up extended attributes that codesign refuses to sign
xattr -cr "$APP"
codesign --force --sign - "$APP"

echo "Done: $(pwd)/$APP"
echo "Drag Timetable.app into your Applications folder."
[ -z "$CI" ] && open -R "$APP"
exit 0
