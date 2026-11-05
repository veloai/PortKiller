// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "PortKillerGame",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "PortKillerGame", targets: ["PortKillerGame"]),
        .executable(name: "PKPRecoveryHelper", targets: ["PKPRecoveryHelper"]),
        .executable(name: "PKPRecoverySetup", targets: ["PKPRecoverySetup"])
    ],
    targets: [
        .executableTarget(
            name: "PortKillerGame",
            dependencies: ["RecoveryShared"],
            path: "Sources"
        ),
        .target(name: "RecoveryShared", path: "RecoveryShared", swiftSettings: [.swiftLanguageMode(.v5)]),
        .systemLibrary(name: "CSQLite", path: "RecoveryCore/CSQLite"),
        .target(name: "RecoveryCore", dependencies: ["RecoveryShared", "CSQLite"], path: "RecoveryCore/Sources", swiftSettings: [.swiftLanguageMode(.v5)]),
        .executableTarget(name: "PKPRecoveryHelper", dependencies: ["RecoveryShared", "RecoveryCore"], path: "RecoveryHelper", swiftSettings: [.swiftLanguageMode(.v5)]),
        .executableTarget(name: "PKPRecoverySetup", dependencies: ["RecoveryShared", "RecoveryCore"], path: "Tools/Setup", swiftSettings: [.swiftLanguageMode(.v5)]),
        .testTarget(name: "RecoveryTests", dependencies: ["RecoveryShared", "RecoveryCore", "CSQLite"], path: "RecoveryTests", swiftSettings: [.swiftLanguageMode(.v5)])
    ]
)
