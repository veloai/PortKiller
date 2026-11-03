import SwiftUI
import AppKit

@MainActor
func savePreview() {
    let view = ContentView()
        .frame(width: 1100, height: 720)
        .background(Color(red: 0.06, green: 0.08, blue: 0.10))
    
    let renderer = ImageRenderer(content: view)
    renderer.scale = 2.0 // Retina 2x
    
    if let image = renderer.nsImage {
        if let tiffData = image.tiffRepresentation,
           let rep = NSBitmapImageRep(data: tiffData),
           let pngData = rep.representation(using: .png, properties: [:]) {
            let path = "/Users/aiso/.gemini/antigravity/brain/a8ac0ef0-4f1c-4885-b8de-22377193f7bf/app_preview.png"
            try? pngData.write(to: URL(fileURLWithPath: path))
            print("SNAPSHOT_SAVED")
        }
    }
}
