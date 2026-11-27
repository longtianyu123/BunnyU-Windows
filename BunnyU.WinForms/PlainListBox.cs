namespace BunnyU.WinForms;

internal sealed class PlainListEntry(string key,string text,object? data=null)
{
    public string Key=key;
    public string Text=text;
    public object? Data=data;
    public override string ToString()=>Text;
}

// Native LISTBOX, with no columns, table provider, or header child window.
internal class PlainListBox:ListBox
{
    public PlainListBox()
    {
        Dock=DockStyle.Fill;IntegralHeight=false;SelectionMode=SelectionMode.One;
        MultiColumn=false;HorizontalScrollbar=true;TabStop=true;
    }
    public PlainListEntry? SelectedEntry=>SelectedItem as PlainListEntry;
    public void SelectIndex(int index)
    {
        if(Items.Count>0)
        {
            SelectedIndex=Math.Clamp(index,0,Items.Count-1);
            var bounds=GetItemRectangle(SelectedIndex);
            if(bounds.Top<0)TopIndex=SelectedIndex;
            else if(bounds.Bottom>ClientSize.Height)TopIndex=Math.Max(0,SelectedIndex-Math.Max(1,ClientSize.Height/ItemHeight)+1);
        }
        Focus();
    }
    public void SetRows(IReadOnlyList<PlainListEntry> rows,string? selectedKey=null)
    {
        selectedKey??=SelectedEntry?.Key;var top=TopIndex;
        if(rows.Count==Items.Count&&rows.Select((row,index)=>Items[index] is PlainListEntry prior&&prior.Key==row.Key&&prior.Text==row.Text).All(equal=>equal))
        {
            // Progress changes only the backing data. Avoid native list mutations,
            // redraw, scrolling and accessibility events for an unchanged list.
            for(var i=0;i<rows.Count;i++)((PlainListEntry)Items[i]).Data=rows[i].Data;
            if(selectedKey!=SelectedEntry?.Key)
            {
                var index=-1;for(var i=0;i<rows.Count;i++)if(rows[i].Key==selectedKey){index=i;break;}
                if(SelectedIndex!=index)SelectedIndex=index;
            }
            return;
        }
        BeginUpdate();
        try
        {
            for(var i=0;i<rows.Count;i++)
            {
                var next=rows[i];
                if(i<Items.Count&&Items[i] is PlainListEntry prior&&prior.Key==next.Key)
                {
                    prior.Data=next.Data;
                    if(prior.Text!=next.Text){prior.Text=next.Text;Items[i]=prior;}
                }
                else Items.Insert(i,next);
            }
            while(Items.Count>rows.Count)Items.RemoveAt(Items.Count-1);
            var selected=-1;for(var i=0;i<Items.Count;i++)if(((PlainListEntry)Items[i]).Key==selectedKey){selected=i;break;}
            if(SelectedIndex!=selected)SelectedIndex=selected;
            if(Items.Count>0&&TopIndex!=Math.Min(top,Items.Count-1))TopIndex=Math.Min(top,Items.Count-1);
        }
        finally{EndUpdate();}
    }
}
