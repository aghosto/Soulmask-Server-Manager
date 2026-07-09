using System.Collections.Generic;

namespace SoulmaskServerManager.Models
{
    public enum ParameterType
    {
        Float,
        Int,
        Bool
    }

    public class ParameterDef
    {
        public string ChineseName { get; set; } = "";
        public string EnglishName { get; set; } = "";
        /// <summary>
        /// Corresponding game internal property name (e.g. "ExpRatio", "JianZhuFuLanMul").
        /// Empty string means this parameter is display-only in the editor (no game property mapping).
        /// </summary>
        public string GameKey { get; set; } = "";
        /// <summary>
        /// 详细描述（来自网站数据）
        /// </summary>
        public string Description { get; set; } = "";
        /// <summary>
        /// 简单描述/Tooltip（来自网站数据）
        /// </summary>
        public string Tooltip { get; set; } = "";
        public double Min { get; set; }
        public double Max { get; set; }
        public double Step { get; set; }
        public ParameterType Type { get; set; }
        /// <summary>
        /// 是否启用（false 表示在编辑器中显示但不可修改）
        /// </summary>
        public bool IsEnabled { get; set; } = true;
    }

    public class ParameterCategory
    {
        public string ChineseName { get; set; } = "";
        public string EnglishName { get; set; } = "";
        public List<ParameterDef> Params { get; set; } = new();
    }
}
