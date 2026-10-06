@echo off
chcp 65001 >nul
echo ========================================
echo   剪贴板历史 - 打包
echo ========================================
echo.

REM WPF 编译 XAML 时会在项目根目录生成临时工程，正常会自动删掉。
REM 一旦上次构建被中断，它会残留下来，导致这次构建报 MSB1011"有多个项目文件"。
if exist "ClipboardHistory_*_wpftmp.csproj" (
    echo [0/3] 清理上次残留的临时工程...
    del /q "ClipboardHistory_*_wpftmp.csproj"
)

if exist "bin\Release" (
    echo [1/3] 清理旧产物...
    rd /s /q "bin\Release"
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

echo [2/3] 正在打包...
echo （依赖 .NET 运行时，产物约 2MB；要拷到没装 .NET 的电脑请看下面的注释）
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

if %errorlevel% neq 0 (
    echo.
    echo 打包失败！请把上面的错误信息发给开发者。
    pause
    exit /b %errorlevel%
)

REM 需要拷到没装 .NET 运行时的电脑？不用改这个文件——直接双击 build-standalone.bat，
REM 它打的是自包含版本（约 150MB，产物在 dist\ 下）。

echo [3/3] 清理临时工程...
del /q "ClipboardHistory_*_wpftmp.csproj" 2>nul

echo.
echo ========================================
echo   打包完成
echo ========================================
echo 产物位置：
echo   bin\Release\net9.0-windows\win-x64\publish\剪贴板历史.exe
echo.
echo 下一步：
echo   1. 双击那个 exe，确认右下角托盘出现淡蓝的剪贴板图标
echo   2. 托盘图标右键 -^> 设置，勾上"开机自动启动"并保存
echo   3. 重启电脑，确认开机后托盘里已经有它
echo.
pause
