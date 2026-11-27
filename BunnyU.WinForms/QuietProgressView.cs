namespace BunnyU.WinForms;

// Paint live progress without changing native text or raising value/name notifications.
// MSAA clients can query the current read-only value when the user reads this control.
internal sealed class QuietProgressView:Control
{
    private string _value="请选择一个下载任务。";
    public string CurrentText=>_value;
    public QuietProgressView()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);
        TabStop=true;BackColor=SystemColors.Window;ForeColor=SystemColors.WindowText;
    }
    public void UpdateProgress(string value){if(_value==value)return;_value=value;Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);ControlPaint.DrawBorder(e.Graphics,ClientRectangle,SystemColors.WindowFrame,ButtonBorderStyle.Solid);
        var area=Rectangle.Inflate(ClientRectangle,-7,-5);
        TextRenderer.DrawText(e.Graphics,_value,Font,area,ForeColor,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-3,-3));
    }
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override AccessibleObject CreateAccessibilityInstance()=>new ProgressAccessibility(this);
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(keyData==(Keys.Control|Keys.C)){try{Clipboard.SetText(_value);}catch(System.Runtime.InteropServices.ExternalException){}return true;}
        return base.ProcessCmdKey(ref msg,keyData);
    }
    private sealed class ProgressAccessibility(QuietProgressView owner):ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role=>AccessibleRole.Text;
        public override string? Value{get=>owner._value;set{}}
        public override AccessibleStates State=>base.State|AccessibleStates.ReadOnly|AccessibleStates.Focusable;
    }
}
