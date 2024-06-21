@echo off

:: rd /s/q Cmake

cmake -S ./SourceCode -B ./Cmake -G "Visual Studio 17 2022"

set /p input=Open the solution? (y/n)

if /i "%input%"=="y" start ./Cmake/CrucibleSolution.sln