using System;
using System.Collections.Generic;
using System.Linq;

namespace A_Pair.Core.Models
{
    public class ClassroomLayoutDefinition : IClassroomLayout
    {
        // 新增 Id 和 Name（接口要求）
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;

        // 使用接口中定义的 LayoutType 枚举
        public LayoutType LayoutType { get; set; } = LayoutType.Grid;

        // 为兼容旧代码保留字符串版本
        public string LayoutTypeString
        {
            get => LayoutType.ToString();
            set => LayoutType = Enum.TryParse<LayoutType>(value, out var result) ? result : LayoutType.Grid;
        }

        // 原有公共可变属性（保持对外的灵活性）
        public List<Seat> Seats { get; set; } = new();
        public List<Obstacle> Obstacles { get; set; } = new();
        public LayoutMetadata Metadata { get; set; } = new GridLayoutMetadata();

        // 显式实现 IClassroomLayout 接口的只读成员
        IReadOnlyList<Seat> IClassroomLayout.Seats => Seats;
        IReadOnlyList<Obstacle> IClassroomLayout.Obstacles => Obstacles;
        LayoutMetadata IClassroomLayout.Metadata => Metadata;
    }
}