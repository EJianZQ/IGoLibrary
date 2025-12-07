#!/bin/bash

# 生产环境快速验证脚本

echo "=========================================="
echo "🔍 生产环境配置验证"
echo "=========================================="
echo ""

# 检查 Mock 开关
echo "1️⃣  检查 Mock 开关状态..."
MOCK_STATUS=$(grep "IsSimulationMode = " IGoLibrary.Mac/App.axaml.cs | grep -o "true\|false")
if [ "$MOCK_STATUS" = "false" ]; then
    echo "   ✅ Mock 开关: 已关闭 (生产模式)"
else
    echo "   ❌ Mock 开关: 仍然开启 (测试模式)"
    echo "   ⚠️  请修改 App.axaml.cs 中的 IsSimulationMode = false"
fi
echo ""

# 检查时间模拟
echo "2️⃣  检查时间模拟状态..."
TIME_STATUS=$(grep "EnableTimeSimulation = " IGoLibrary.Mac/ViewModels/GrabSeatViewModel.cs | grep -o "true\|false")
if [ "$TIME_STATUS" = "false" ]; then
    echo "   ✅ 时间模拟: 已禁用 (使用真实时间)"
else
    echo "   ❌ 时间模拟: 仍然启用 (使用模拟时间)"
    echo "   ⚠️  请修改 GrabSeatViewModel.cs 中的 EnableTimeSimulation = false"
fi
echo ""

# 总结
echo "=========================================="
if [ "$MOCK_STATUS" = "false" ] && [ "$TIME_STATUS" = "false" ]; then
    echo "✅ 生产环境配置正确！"
    echo "=========================================="
    echo ""
    echo "📋 下一步操作："
    echo "   1. 重新编译: cd IGoLibrary.Mac && dotnet clean && dotnet build"
    echo "   2. 启动应用: ./启动应用.sh"
    echo "   3. 验证日志: 应该看到 '🌐 [真实模式] 已启用'"
    echo ""
else
    echo "❌ 生产环境配置有误！"
    echo "=========================================="
    echo ""
    echo "⚠️  请按照上方提示修复配置，然后重新运行此脚本验证"
    echo ""
fi
