import SwiftUI
import AppKit

@MainActor
func savePreview() {
    let artifactDir = "/Users/aiso/.gemini/antigravity/brain/7fccda1c-720e-4813-8b6e-5ae25e689459"
    try? FileManager.default.createDirectory(atPath: artifactDir, withIntermediateDirectories: true)

    // 1. Studio View Snapshot
    let studioView = ContentView()
        .frame(width: 1100, height: 720)
        .background(Color(red: 0.06, green: 0.08, blue: 0.10))
    
    let studioRenderer = ImageRenderer(content: studioView)
    studioRenderer.scale = 2.0
    if let image = studioRenderer.nsImage,
       let tiffData = image.tiffRepresentation,
       let rep = NSBitmapImageRep(data: tiffData),
       let pngData = rep.representation(using: .png, properties: [:]) {
        try? pngData.write(to: URL(fileURLWithPath: "\(artifactDir)/app_preview.png"))
        print("STUDIO_SNAPSHOT_SAVED")
    }

    // 2. Mac Desktop Pet Snapshot
    let petView = MacPetView()
        .frame(width: 240, height: 220)
        .background(Color.clear)

    let petRenderer = ImageRenderer(content: petView)
    petRenderer.scale = 2.0
    if let image = petRenderer.nsImage,
       let tiffData = image.tiffRepresentation,
       let rep = NSBitmapImageRep(data: tiffData),
       let pngData = rep.representation(using: .png, properties: [:]) {
        try? pngData.write(to: URL(fileURLWithPath: "\(artifactDir)/pet_preview.png"))
        print("PET_SNAPSHOT_SAVED")
    }
}
