use std::fs;

#[test]
fn cargo_manifest_declares_native_overlay_gui_stack() {
    let manifest_path = format!("{}/Cargo.toml", env!("CARGO_MANIFEST_DIR"));
    let manifest = fs::read_to_string(manifest_path).expect("Cargo.toml should be readable");

    assert!(
        manifest.contains("eframe"),
        "Rust smoke overlay must declare eframe so the binary can render a native operator window"
    );
    assert!(
        manifest.contains("egui"),
        "Rust smoke overlay must declare egui so suite selection and review controls are real UI widgets"
    );
}
