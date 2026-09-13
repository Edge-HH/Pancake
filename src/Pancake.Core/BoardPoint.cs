namespace Pancake;

/// <summary>
/// 与界面框架无关的点坐标，用于笔迹、导出排版等核心层几何计算。
/// </summary>
public readonly record struct BoardPoint(double X, double Y);
