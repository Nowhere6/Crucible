file(GLOB SOURCES "*.cs" "Core/*.cs")

add_executable(SharpD12 ${SOURCES})

set(NUGET_REF "
  SharpDX_4.2.0;
  SharpDX.DXGI_4.2.0;
  SharpDX.D3DCompiler_4.2.0;
  SharpDX.Mathematics_4.2.0;
  SharpDX.Direct3D12_4.2.0;
  SharpDX.Desktop_4.2.0;
  SharpDX.Direct2D1_4.2.0;
  RawInput.Sharp_0.1.3;
")

set_property(TARGET SharpD12 PROPERTY VS_PACKAGE_REFERENCES ${NUGET_REF})

set_target_properties(SharpD12 PROPERTIES
  WIN32_EXECUTABLE true
  VS_GLOBAL_UseWPF true
  VS_GLOBAL_UseWindowsForms true
)