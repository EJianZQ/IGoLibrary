#!/bin/bash

# ========================================
# IGoLibrary 一键启动脚本（生产环境版本）
# ========================================
# 功能：
# 1. 自动检测并安装 .NET SDK
# 2. 验证生产环境配置（Mock 开关、时间模拟）
# 3. 编译并运行 IGoLibrary.Mac 项目
# 4. 显示友好的启动信息
# ========================================

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m' # No Color

# 清屏
clear

# 显示欢迎信息
echo ""
echo "=========================================="
echo -e "${CYAN}🎯 IGoLibrary 图书馆抢座系统${NC}"
echo "=========================================="
echo ""

# 获取脚本所在目录
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_DIR="$SCRIPT_DIR/IGoLibrary.Mac"

# 检查项目目录是否存在
if [ ! -d "$PROJECT_DIR" ]; then
    echo -e "${RED}❌ 错误: 找不到项目目录 IGoLibrary.Mac${NC}"
    echo -e "${YELLOW}   当前脚本位置: $SCRIPT_DIR${NC}"
    echo ""
    read -p "按任意键退出..."
    exit 1
fi

# 检查 .NET SDK 是否已安装
echo -e "${BLUE}🔍 检查 .NET SDK...${NC}"
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    echo -e "${GREEN}✅ .NET SDK 已安装 (版本: $DOTNET_VERSION)${NC}"
else
    echo -e "${RED}❌ 未检测到 .NET SDK${NC}"
    echo -e "${YELLOW}📥 正在尝试安装 .NET SDK...${NC}"

    # 检查是否安装了 Homebrew
    if command -v brew &> /dev/null; then
        echo -e "${BLUE}   使用 Homebrew 安装 .NET SDK...${NC}"
        brew install --cask dotnet-sdk

        if [ $? -eq 0 ]; then
            echo -e "${GREEN}✅ .NET SDK 安装成功${NC}"
        else
            echo -e "${RED}❌ .NET SDK 安装失败${NC}"
            echo -e "${YELLOW}   请手动安装: https://dotnet.microsoft.com/download${NC}"
            echo ""
            read -p "按任意键退出..."
            exit 1
        fi
    else
        echo -e "${RED}❌ 未检测到 Homebrew${NC}"
        echo -e "${YELLOW}   请先安装 Homebrew 或手动安装 .NET SDK${NC}"
        echo -e "${YELLOW}   Homebrew: https://brew.sh${NC}"
        echo -e "${YELLOW}   .NET SDK: https://dotnet.microsoft.com/download${NC}"
        echo ""
        read -p "按任意键退出..."
        exit 1
    fi
fi

echo ""

# ========================================
# 🔍 生产环境配置验证（关键步骤）
# ========================================
echo "=========================================="
echo -e "${MAGENTA}🔍 生产环境配置验证${NC}"
echo "=========================================="

CONFIG_ERROR=0

# 检查 Mock 开关状态
echo -e "${BLUE}1️⃣  检查 Mock 开关...${NC}"
APP_FILE="$PROJECT_DIR/App.axaml.cs"
MOCK_STATUS=$(grep "private const bool IsSimulationMode = " "$APP_FILE" | grep -o "true\|false")

if [ "$MOCK_STATUS" = "false" ]; then
    echo -e "${GREEN}   ✅ Mock 开关: 已关闭（生产模式）${NC}"
    echo -e "${CYAN}      - 使用 GetLibInfoServiceImpl（真实座位信息）${NC}"
    echo -e "${CYAN}      - 使用 PrereserveSeatServiceImpl（真实预约服务）${NC}"
    echo -e "${CYAN}      - 连接真实服务器: wechat.v2.traceint.com${NC}"
elif [ "$MOCK_STATUS" = "true" ]; then
    echo -e "${RED}   ❌ Mock 开关: 仍然开启（测试模式）${NC}"
    echo -e "${YELLOW}      ⚠️  当前使用模拟服务，无法连接真实服务器！${NC}"
    echo -e "${YELLOW}      ⚠️  请修改 App.axaml.cs 中的 IsSimulationMode = false${NC}"
    CONFIG_ERROR=1
else
    echo -e "${RED}   ❌ 无法检测 Mock 开关状态${NC}"
    CONFIG_ERROR=1
fi

echo ""

# 检查时间模拟状态
echo -e "${BLUE}2️⃣  检查时间模拟...${NC}"
VIEWMODEL_FILE="$PROJECT_DIR/ViewModels/GrabSeatViewModel.cs"
TIME_STATUS=$(grep "private const bool EnableTimeSimulation = " "$VIEWMODEL_FILE" | grep -o "true\|false")

if [ "$TIME_STATUS" = "false" ]; then
    echo -e "${GREEN}   ✅ 时间模拟: 已禁用（使用真实时间）${NC}"
    echo -e "${CYAN}      - GetBeijingNow() 返回真实北京时间${NC}"
    echo -e "${CYAN}      - 19:59:50 准备，20:00:00 准时开始抢座${NC}"
elif [ "$TIME_STATUS" = "true" ]; then
    echo -e "${RED}   ❌ 时间模拟: 仍然启用（使用模拟时间）${NC}"
    echo -e "${YELLOW}      ⚠️  当前时间固定在 19:59:45，无法正常抢座！${NC}"
    echo -e "${YELLOW}      ⚠️  请修改 GrabSeatViewModel.cs 中的 EnableTimeSimulation = false${NC}"
    CONFIG_ERROR=1
else
    echo -e "${RED}   ❌ 无法检测时间模拟状态${NC}"
    CONFIG_ERROR=1
fi

echo ""
echo "=========================================="

# 如果配置有误，提示用户并询问是否继续
if [ $CONFIG_ERROR -eq 1 ]; then
    echo -e "${RED}❌ 生产环境配置验证失败！${NC}"
    echo "=========================================="
    echo ""
    echo -e "${YELLOW}⚠️  检测到配置错误，当前不是生产环境！${NC}"
    echo ""
    echo -e "${CYAN}建议操作：${NC}"
    echo "  1. 按 Ctrl+C 退出"
    echo "  2. 修改配置文件："
    echo "     - App.axaml.cs: IsSimulationMode = false"
    echo "     - GrabSeatViewModel.cs: EnableTimeSimulation = false"
    echo "  3. 重新运行此脚本"
    echo ""
    echo -e "${YELLOW}或者，如果你确定要在测试模式下运行，请输入 'yes' 继续${NC}"
    echo ""
    read -p "是否继续启动？(yes/no): " CONTINUE

    if [ "$CONTINUE" != "yes" ]; then
        echo ""
        echo -e "${CYAN}已取消启动。请修复配置后重试。${NC}"
        echo ""
        read -p "按任意键退出..."
        exit 1
    fi

    echo ""
    echo -e "${YELLOW}⚠️  继续以测试模式启动...${NC}"
    echo ""
else
    echo -e "${GREEN}✅ 生产环境配置验证通过！${NC}"
    echo "=========================================="
    echo ""
    echo -e "${GREEN}🎯 系统已准备好实战模式：${NC}"
    echo -e "${CYAN}   ✓ 使用真实服务器和真实数据${NC}"
    echo -e "${CYAN}   ✓ 使用真实北京时间${NC}"
    echo -e "${CYAN}   ✓ 可以正常抢座${NC}"
    echo ""
fi

# 进入项目目录
cd "$PROJECT_DIR"

# 显示启动信息
echo "=========================================="
echo -e "${GREEN}🚀 正在启动应用...${NC}"
echo "=========================================="
echo -e "${YELLOW}📂 项目目录: $PROJECT_DIR${NC}"
echo -e "${YELLOW}⏰ 启动时间: $(date '+%Y-%m-%d %H:%M:%S')${NC}"
if [ "$MOCK_STATUS" = "false" ] && [ "$TIME_STATUS" = "false" ]; then
    echo -e "${GREEN}🌐 运行模式: 生产环境（真实模式）${NC}"
else
    echo -e "${YELLOW}🎭 运行模式: 测试环境（模拟模式）${NC}"
fi
echo "=========================================="
echo ""

# 清理并重新编译（确保配置生效）
echo -e "${BLUE}🔨 清理旧编译文件...${NC}"
dotnet clean > /dev/null 2>&1

echo -e "${BLUE}🔨 重新编译项目...${NC}"
dotnet build --configuration Release > /dev/null 2>&1

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ 编译失败！${NC}"
    echo ""
    echo -e "${YELLOW}正在显示详细编译信息...${NC}"
    echo ""
    dotnet build --configuration Release
    echo ""
    read -p "按任意键退出..."
    exit 1
fi

echo -e "${GREEN}✅ 编译成功${NC}"
echo ""

# 运行项目
echo -e "${BLUE}🚀 启动应用...${NC}"
echo ""
echo "=========================================="
echo -e "${CYAN}应用启动后，请验证以下内容：${NC}"
if [ "$MOCK_STATUS" = "false" ] && [ "$TIME_STATUS" = "false" ]; then
    echo -e "${GREEN}✓ 启动日志应显示: 🌐 [真实模式] 已启用${NC}"
    echo -e "${GREEN}✓ 图书馆列表应显示真实名称（不是"模拟图书馆"）${NC}"
    echo -e "${GREEN}✓ 日志不应包含 [模拟] 前缀${NC}"
else
    echo -e "${YELLOW}✓ 启动日志应显示: 🎭 [模拟模式] 已启用${NC}"
    echo -e "${YELLOW}✓ 图书馆列表会显示"模拟图书馆"${NC}"
    echo -e "${YELLOW}✓ 日志会包含 [模拟] 前缀${NC}"
fi
echo "=========================================="
echo ""

# 使用 dotnet run 启动项目
dotnet run --configuration Release

# 检查退出状态
EXIT_CODE=$?

echo ""
echo "=========================================="
if [ $EXIT_CODE -eq 0 ]; then
    echo -e "${GREEN}✅ 应用已正常退出${NC}"
else
    echo -e "${RED}❌ 应用退出异常 (退出码: $EXIT_CODE)${NC}"
fi
echo "=========================================="
echo ""

# 等待用户按键
read -p "按任意键关闭窗口..."
