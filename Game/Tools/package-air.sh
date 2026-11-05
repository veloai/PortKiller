#!/bin/bash
# Run only in the approved build/install batch. No compiler or service command runs on source review.
set -euo pipefail
PKP_GAME_DIR="$(cd "$(dirname "$0")/.." && pwd -P)"
PKP_RELEASE_DIR="$PKP_GAME_DIR/.build/release"
PKP_STAGE_ROOT="$(mktemp -d "$HOME/Applications/.pkp-air.XXXXXX")"
PKP_STAGE_APP="$PKP_STAGE_ROOT/PortKillerPet.app"
PKP_INSTALLED_APP="$HOME/Applications/PortKillerPet.app"
PKP_BACKUP_APP="$HOME/Applications/PortKillerPet.before-recovery-$(date +%Y%m%d-%H%M%S).app"
PKP_REPLACED=0
pkp_cleanup() {
    if [ "$PKP_REPLACED" = 0 ] && [ ! -e "$PKP_INSTALLED_APP" ] && [ -d "$PKP_BACKUP_APP" ]; then
        mv "$PKP_BACKUP_APP" "$PKP_INSTALLED_APP"
    fi
    rm -rf "$PKP_STAGE_ROOT"
}
trap pkp_cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
for PKP_BINARY in PortKillerGame PKPRecoveryHelper PKPRecoverySetup; do
    test -x "$PKP_RELEASE_DIR/$PKP_BINARY"
done
test -d "$PKP_INSTALLED_APP"
test ! -L "$PKP_INSTALLED_APP"
test ! -e "$HOME/Library/LaunchAgents/com.veloai.portkiller.recovery.plist"
test ! -e "$HOME/Library/Application Support/PortKillerRecovery/policy.json"
# Keep the installed character assets and bundle metadata; replace only owned executables.
ditto "$PKP_INSTALLED_APP" "$PKP_STAGE_APP"
mkdir -p "$PKP_STAGE_APP/Contents/Helpers"
cp "$PKP_RELEASE_DIR/PortKillerGame" "$PKP_STAGE_APP/Contents/MacOS/PortKillerPet"
cp "$PKP_RELEASE_DIR/PKPRecoveryHelper" "$PKP_STAGE_APP/Contents/Helpers/PKPRecoveryHelper"
cp "$PKP_RELEASE_DIR/PKPRecoverySetup" "$PKP_STAGE_APP/Contents/Helpers/PKPRecoverySetup"
codesign --force --sign - "$PKP_STAGE_APP/Contents/Helpers/PKPRecoveryHelper"
codesign --force --sign - "$PKP_STAGE_APP/Contents/Helpers/PKPRecoverySetup"
codesign --force --sign - --identifier com.veloai.portkiller.pet "$PKP_STAGE_APP"
codesign --verify --strict "$PKP_STAGE_APP"
test ! -e "$PKP_BACKUP_APP"
mv "$PKP_INSTALLED_APP" "$PKP_BACKUP_APP"
mv "$PKP_STAGE_APP" "$PKP_INSTALLED_APP"
PKP_REPLACED=1
if ! "$PKP_INSTALLED_APP/Contents/Helpers/PKPRecoverySetup" install; then
    # Existing services are untouched. Keep both builds and any setup receipt for review.
    printf '%s\n' "설치 미완료.이전 앱 보존: $PKP_BACKUP_APP" >&2
    exit 1
fi
printf '%s\n' "새 PKP 앱·복구 도우미 설치 완료.이전 앱 보존: $PKP_BACKUP_APP"
printf '%s\n' "기존 실행 중 앱은 자동 종료하지 않습니다.승인한 앱 재기동 단계에서 전환하세요."
