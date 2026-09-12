using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Pancake.Controls;

/// <summary>所有功能图标的语义名称与随程序分发的字体版本一一对应。</summary>
public static class FluentGlyphs
{
    public const string Board = "\uE20D"; // ic_fluent_board_20_regular
    public const string Folder = "\uE875"; // ic_fluent_folder_20_regular
    public const string Weather = "\uF465"; // ic_fluent_weather_sunny_20_regular
    public const string Microphone = "\uEB80"; // ic_fluent_mic_20_regular
    public const string Color = "\uE51E"; // ic_fluent_color_20_regular
    public const string Components = "\uE06F"; // ic_fluent_apps_20_regular
    public const string Info = "\uE9E4"; // ic_fluent_info_20_regular
    public const string Back = "\uE109"; // ic_fluent_arrow_left_20_regular
    public const string Dismiss = "\uE671"; // ic_fluent_dismiss_20_regular
    public const string Add = "\uE00D"; // ic_fluent_add_20_regular
    public const string Grid = "\uE929"; // ic_fluent_grid_20_regular
    public const string Layout = "\uEA39"; // ic_fluent_layout_cell_four_20_regular
    public const string Edit = "\uE7C9"; // ic_fluent_edit_20_regular
    public const string Settings = "\uEF27"; // ic_fluent_settings_20_regular
    public const string FullScreen = "\uE8D1"; // ic_fluent_full_screen_maximize_20_regular
    public const string ExitFullScreen = "\uE8D3"; // ic_fluent_full_screen_minimize_20_regular
    public const string Checkmark = "\uE424"; // ic_fluent_checkmark_20_regular
    public const string Undo = "\uE195"; // ic_fluent_arrow_undo_20_regular
    public const string Delete = "\uE61D"; // ic_fluent_delete_20_regular
    public const string ImageAdd = "\uE9B4"; // ic_fluent_image_add_20_regular
    public const string Bold = "\uF1F2"; // ic_fluent_text_bold_20_regular
    public const string Italic = "\uF2C7"; // ic_fluent_text_italic_20_regular
    public const string Underline = "\uF315"; // ic_fluent_text_underline_20_regular
    public const string Highlight = "\uE989"; // ic_fluent_highlight_20_regular
    public const string Crop = "\uE59B"; // ic_fluent_crop_20_regular
    public const string Rotate = "\uE133"; // ic_fluent_arrow_rotate_clockwise_20_regular
    public const string Reset = "\uE12F"; // ic_fluent_arrow_reset_20_regular
    public const string Image = "\uE9B2"; // ic_fluent_image_20_regular
    public const string Pdf = "\uE719"; // ic_fluent_document_pdf_20_regular
    public const string Document = "\uE687"; // ic_fluent_document_20_regular
    public const string Pen = "\uEC9D"; // ic_fluent_pen_20_regular
    public const string Eraser = "\uE7FF"; // ic_fluent_eraser_20_regular
    public const string TextGrammarWand = "\uF285"; // ic_fluent_text_grammar_wand_20_regular
    // 竖版控制窗里字体选择框收成图标按钮时用的字体图标。
    public const string TextFont = "\uF26F"; // ic_fluent_text_font_20_regular
    // 作业板缩放控件用的放大/缩小图标。
    public const string ZoomIn = "\uF4D1"; // ic_fluent_zoom_in_20_regular
    public const string ZoomOut = "\uF4D3"; // ic_fluent_zoom_out_20_regular
    // 组件右下角缩放手柄的方向提示；同时用于校验该字形确实随字体分发。
    public const string ArrowDownRight = "\uE0D1"; // ic_fluent_arrow_down_right_20_regular
    public static string Resolve(string symbol) => symbol switch
    {
        nameof(Board) => Board,
        nameof(Folder) => Folder,
        nameof(Weather) => Weather,
        nameof(Microphone) => Microphone,
        nameof(Color) => Color,
        nameof(Components) => Components,
        nameof(Info) => Info,
        nameof(Back) => Back,
        nameof(Dismiss) => Dismiss,
        nameof(Add) => Add,
        nameof(Grid) => Grid,
        nameof(Layout) => Layout,
        nameof(Edit) => Edit,
        nameof(Settings) => Settings,
        nameof(FullScreen) => FullScreen,
        nameof(ExitFullScreen) => ExitFullScreen,
        nameof(Checkmark) => Checkmark,
        nameof(Undo) => Undo,
        nameof(Delete) => Delete,
        nameof(ImageAdd) => ImageAdd,
        nameof(Bold) => Bold,
        nameof(Italic) => Italic,
        nameof(Underline) => Underline,
        nameof(Highlight) => Highlight,
        nameof(Crop) => Crop,
        nameof(Rotate) => Rotate,
        nameof(Reset) => Reset,
        nameof(Image) => Image,
        nameof(Pdf) => Pdf,
        nameof(Document) => Document,
        nameof(Pen) => Pen,
        nameof(Eraser) => Eraser,
        nameof(TextGrammarWand) => TextGrammarWand,
        nameof(TextFont) => TextFont,
        nameof(ZoomIn) => ZoomIn,
        nameof(ZoomOut) => ZoomOut,
        nameof(ArrowDownRight) => ArrowDownRight,
        _ => throw new ArgumentException("未知图标名称", nameof(symbol))
    };
}

public sealed class FluentIcon : FontIcon
{
    public FluentIcon()
    {
        FontFamily = new FontFamily("ms-appx:///Assets/Fonts/FluentSystemIcons-Resizable.ttf#FluentSystemIcons-Resizable");
        // 默认值不会触发依赖属性回调，必须初始化默认的“关于”图标。
        Glyph = FluentGlyphs.Info;
    }
    public static readonly DependencyProperty SymbolProperty = DependencyProperty.Register(nameof(Symbol), typeof(string), typeof(FluentIcon), new PropertyMetadata("Info", (sender, args) => ((FluentIcon)sender).Glyph = FluentGlyphs.Resolve((string)args.NewValue)));
    public string Symbol { get => (string)GetValue(SymbolProperty); set => SetValue(SymbolProperty, value); }
}
