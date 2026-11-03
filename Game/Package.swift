// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "PortKillerGame",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "PortKillerGame", targets: ["PortKillerGame"])
    ],
    targets: [
        .executableTarget(
            name: "PortKillerGame",
            path: "Sources"
        )
    ]
)
