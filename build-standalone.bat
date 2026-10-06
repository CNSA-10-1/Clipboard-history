@echo off
chcp 65001 >nul
echo ========================================
echo   剪贴板历史 - 打包（无依赖版）
echo ========================================
echo.
echo 这个版本把 .NET 运行时一起打了进去，
echo 拷到任何 64 位 Windows 10/11 上直接双击就能跑，不用先装 .NET。
echo 代价是体积大（约 150MB）。
echo.
echo 只给自己这台电脑用的话，用 build.bat 那个小体积版就行。
echo.

REM WPF 编译 XAML 时会在项目根目录生成临时工程，正常会自动删掉。
REM 一旦上次构建被中断，它会残留下来，导致这次构建报 MSB1011"有多个项目文件"。
if exist "ClipboardHistory_*_wpftmp.csproj" (
    echo [0/3] 清理上次残留的临时工程...
    del /q "ClipboardHistory_*_wpftmp.csproj"
)

if exist "dist" (
    echo [1/3] 清理旧的发布目录...
    rd /s /q "dist"
)

REM 确认程序已经退出，否则 exe 被占用会打包失败。
REM ⚠️ 这里用 Get-Process 而不是 tasklist：tasklist 对中文进程名会静默匹配失败，
REM 返回空、看起来像"没在跑"，结果打包到一半才报文件占用。
powershell -NoProfile -Command "if (Get-Process 剪贴板历史 -ErrorAction SilentlyContinue) { exit 1 }"
if errorlevel 1 (
    echo.
    echo 检测到程序正在运行。请先从托盘图标右键退出，再重新打包。
    pause
    exit /b 1
)

echo [2/3] 正在打包，要一两分钟...
REM 参数各有原因，不要随手删：
REM   DebugType=none                     不生成 .pdb 调试符号。发布版要的是"就一个 exe"
REM   EnableCompressionInSingleFile=true 把打包进去的所有程序集压缩。这是体积能减半的关键
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:EnableCompressionInSingleFile=true -o dist

if %errorlevel% neq 0 (
    echo.
    echo 打包失败！请把上面的错误信息发给开发者。
    pause
    exit /b %errorlevel%
)

del /q "ClipboardHistory_*_wpftmp.csproj" 2>nul

echo.
echo ========================================
echo   打包完成
echo ========================================
echo 产物：dist\剪贴板历史.exe
echo.
echo 下一步：
echo   1. 双击那个 exe，确认右下角托盘出现图标
echo   2. 想让它开机自启：托盘右键 -^> 设置，勾上"开机自动启动"并保存
echo   3. 要拷到别的电脑：把 dist 里的 exe 拷过去就行
echo.
echo   注意：这个 exe 是打包产物，重新打包会把它删掉重建。
echo   想长期用，请先把它挪到一个固定位置（比如 C:\Tools\剪贴板历史\）。
echo.
pause
