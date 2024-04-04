cmake_minimum_required(VERSION 3.27)

set(CMAKE_DOTNET_SDK "Microsoft.NET.Sdk")

set(CMAKE_DOTNET_TARGET_FRAMEWORK "net8.0-windows") # "net7.0-windows"

project(SharpD12Solution LANGUAGES CSharp)

add_subdirectory(SharpD12)

set_property(DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR} PROPERTY VS_STARTUP_PROJECT SharpD12)

file(COPY ${CMAKE_CURRENT_SOURCE_DIR}/launchSettings.json
  DESTINATION ${CMAKE_CURRENT_BINARY_DIR}/SharpD12/Properties
) # Add native debug profile for user convenience