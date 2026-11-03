import SwiftUI
import AppKit

@MainActor
func savePreview() {
    let artifactDir = "/Users/aiso/.gemini/antigravity/brain/7fccda1c-720e-4813-8b6e-5ae25e689459"
    try? FileManager.default.createDirectory(atPath: artifactDir, withIntermediateDirectories: true)

    // 1. 상태창(메뉴바) 안의 도트 펫 렌더링 스냅샷 (실제 macOS 메뉴바 다크 배경 시뮬레이션)
    let barImg = MenuBarPetController.shared.generateBarImage()

    // 3배 확대하여 도트 느낌을 살린 메뉴바 뷰 생성
    let menuBarMock = ZStack {
        // macOS 상단 메뉴바 어두운 글래스 배경
        Color(red: 0.12, green: 0.13, blue: 0.15)

        HStack {
            Text("").font(.system(size: 14)).foregroundColor(.white)
            Text("Antigravity").font(.system(size: 13, weight: .bold)).foregroundColor(.white)
            Text("File  Edit  View  Window").font(.system(size: 13)).foregroundColor(.gray)

            Spacer()

            // 상태창 안에서 걸어다니는 도트 펫
            Image(nsImage: barImg)
                .interpolation(.none)
                .padding(.horizontal, 4)
                .background(Color.white.opacity(0.08))
                .cornerRadius(4)

            Text("Tue Nov 3  3:30 PM").font(.system(size: 12)).foregroundColor(.white)
        }
        .padding(.horizontal, 14)
    }
    .frame(width: 700, height: 28)

    let barRenderer = ImageRenderer(content: menuBarMock)
    barRenderer.scale = 2.0
    if let image = barRenderer.nsImage,
       let tiffData = image.tiffRepresentation,
       let rep = NSBitmapImageRep(data: tiffData),
       let pngData = rep.representation(using: .png, properties: [:]) {
        try? pngData.write(to: URL(fileURLWithPath: "\(artifactDir)/menubar_pet_preview.png"))
        print("MENUBAR_PET_SNAPSHOT_SAVED")
    }

    // 2. 상태창 펫 팝오버 상세 뷰
    let popoverView = MenuBarPetPopoverView()
        .background(Color(NSColor.windowBackgroundColor))

    let popoverRenderer = ImageRenderer(content: popoverView)
    popoverRenderer.scale = 2.0
    if let image = popoverRenderer.nsImage,
       let tiffData = image.tiffRepresentation,
       let rep = NSBitmapImageRep(data: tiffData),
       let pngData = rep.representation(using: .png, properties: [:]) {
        try? pngData.write(to: URL(fileURLWithPath: "\(artifactDir)/menubar_popover_preview.png"))
        print("MENUBAR_POPOVER_SNAPSHOT_SAVED")
    }
}
