#!/bin/bash

# 自动化测试脚本
# 测试从URL获取Cookie的功能

echo "开始测试 IGoLibrary 功能..."
echo ""

# 从URL中提取code
URL="http://wechat.v2.traceint.com/index.php/graphql/?operationName=index&query=query%7BuserAuth%7BtongJi%7Brank%7D%7D%7D&code=031BRF0w3VOW763IOX1w3FDTyi3BRF0y&state=1"
CODE="031BRF0w3VOW763IOX1w3FDTyi3BRF0y"

echo "URL: $URL"
echo "提取的 Code: $CODE"
echo ""

# 运行控制台测试程序
cd /Users/apple/PycharmProjects/IGoLibrary

# 使用 expect 或者直接通过管道输入命令
echo "1" | /usr/local/share/dotnet/dotnet run --project IGoLibrary.ConsoleTest/IGoLibrary.ConsoleTest.csproj
