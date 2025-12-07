using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IGoLibrary.Core.Data
{
    public class SeatKeyData
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;

        // 优先级：0=主选，1=备选1，2=备选2...
        public int Priority { get; set; } = 0;

        // 优先级显示文本
        public string PriorityText => Priority == 0 ? "主选" : $"备选{Priority}";

        // 背景颜色 - 根据状态自动设置
        public string BackgroundColor => Status == "无人" ? "#1B4D3E" : "#4D1B1B";

        // 状态标签颜色 - 根据状态自动设置
        public string BadgeColor => Status == "无人" ? "#10B981" : "#EF4444";

        // 优先级标签颜色 - 主选用金色，备选用蓝色
        public string PriorityBadgeColor => Priority == 0 ? "#F59E0B" : "#3B82F6";
    }
}
