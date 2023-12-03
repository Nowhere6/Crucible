@echo off

:: rd /s/q Cmake

cmake -S ./SourceCode -B ./Cmake

set /p input=Open the solution? (y/n)

if /i "%input%"=="y" start ./Cmake/SharpD12Solution.sln