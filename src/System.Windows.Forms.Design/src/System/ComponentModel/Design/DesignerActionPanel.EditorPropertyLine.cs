﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Drawing;
using System.Drawing.Design;
using System.Globalization;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace System.ComponentModel.Design;

internal sealed partial class DesignerActionPanel
{
    private sealed partial class EditorPropertyLine : TextBoxPropertyLine, IWindowsFormsEditorService, IServiceProvider
    {
        private EditorButton _button;
        private UITypeEditor _editor;
        private bool _hasSwatch;
        private Image _swatch;
        private FlyoutDialog _dropDownHolder;
        private bool _ignoreNextSelectChange;
        private bool _ignoreDropDownValue;

        public EditorPropertyLine(IServiceProvider serviceProvider, DesignerActionPanel actionPanel)
            : base(serviceProvider, actionPanel)
        {
        }

        private unsafe void ActivateDropDown()
        {
            if (_editor is not null)
            {
                try
                {
                    object newValue = _editor.EditValue(TypeDescriptorContext, this, Value);
                    SetValue(newValue);
                }
                catch (Exception ex)
                {
                    ActionPanel.ShowError(string.Format(SR.DesignerActionPanel_ErrorActivatingDropDown, ex.Message));
                }
            }
            else
            {
                ListBox listBox = new ListBox
                {
                    BorderStyle = BorderStyle.None,
                    IntegralHeight = false,
                    Font = ActionPanel.Font
                };
                listBox.SelectedIndexChanged += new EventHandler(OnListBoxSelectedIndexChanged);
                listBox.KeyDown += new KeyEventHandler(OnListBoxKeyDown);

                TypeConverter.StandardValuesCollection standardValues = GetStandardValues();
                if (standardValues is not null)
                {
                    foreach (object o in standardValues)
                    {
                        string newItem = PropertyDescriptor.Converter.ConvertToString(TypeDescriptorContext, CultureInfo.CurrentCulture, o);
                        listBox.Items.Add(newItem);

                        if ((o is not null) && o.Equals(Value))
                        {
                            listBox.SelectedItem = newItem;
                        }
                    }
                }

                // All measurement code borrowed from WinForms PropertyGridView.cs
                int maxWidth = 0;

                // The listbox draws with GDI, not GDI+.  So, we use a normal DC here.
                using (var hdc = new GetDcScope((HWND)listBox.Handle))
                {
                    using PInvoke.ObjectScope hFont = new(listBox.Font.ToHFONT());
                    using PInvoke.SelectObjectScope fontSelection = new(hdc, hFont);

                    TEXTMETRICW tm = default;

                    if (listBox.Items.Count > 0)
                    {
                        foreach (string s in listBox.Items)
                        {
                            fixed (char* ps = s)
                            {
                                Size textSize = default;
                                PInvoke.GetTextExtentPoint32W(hdc, ps, s.Length, (SIZE*)(void*)&textSize);
                                maxWidth = Math.Max(textSize.Width, maxWidth);
                            }
                        }
                    }

                    PInvoke.GetTextMetrics(hdc, &tm);

                    // border + padding + scrollbar
                    maxWidth += 2 + tm.tmMaxCharWidth + SystemInformation.VerticalScrollBarWidth;

                    listBox.Height = Math.Max(tm.tmHeight + 2, Math.Min(ListBoxMaximumHeight, listBox.PreferredHeight));
                    listBox.Width = Math.Max(maxWidth, EditRegionSize.Width);
                    _ignoreDropDownValue = false;
                }

                try
                {
                    ShowDropDown(listBox, SystemColors.ControlDark);
                }
                finally
                {
                    listBox.SelectedIndexChanged -= new EventHandler(OnListBoxSelectedIndexChanged);
                    listBox.KeyDown -= new KeyEventHandler(OnListBoxKeyDown);
                }

                if (!_ignoreDropDownValue)
                {
                    if (listBox.SelectedItem is not null)
                    {
                        SetValue(listBox.SelectedItem);
                    }
                }
            }
        }

        protected override void AddControls(List<Control> controls)
        {
            base.AddControls(controls);

            _button = new EditorButton();
            _button.Click += new EventHandler(OnButtonClick);
            _button.GotFocus += new EventHandler(OnButtonGotFocus);

            controls.Add(_button);
        }

        private void CloseDropDown()
        {
            if (_dropDownHolder is not null)
            {
                _dropDownHolder.Visible = false;
            }
        }

        protected override int GetTextBoxLeftPadding(int textBoxHeight)
        {
            if (_hasSwatch)
            {
                return base.GetTextBoxLeftPadding(textBoxHeight) + textBoxHeight + 2 * EditorLineSwatchPadding;
            }
            else
            {
                return base.GetTextBoxLeftPadding(textBoxHeight);
            }
        }

        protected override int GetTextBoxRightPadding(int textBoxHeight) => base.GetTextBoxRightPadding(textBoxHeight) + textBoxHeight + 2 * EditorLineButtonPadding;

        protected override bool IsReadOnly()
        {
            if (base.IsReadOnly())
            {
                return true;
            }

            // If we can't convert from string, we are readonly because we can't convert the user's input
            bool converterReadOnly = !PropertyDescriptor.Converter.CanConvertFrom(TypeDescriptorContext, typeof(string));

            // If standard values are supported and are exclusive, we are readonly
            bool standardValuesExclusive =
                PropertyDescriptor.Converter.GetStandardValuesSupported(TypeDescriptorContext) &&
                PropertyDescriptor.Converter.GetStandardValuesExclusive(TypeDescriptorContext);

            return converterReadOnly || standardValuesExclusive;
        }

        public override Size LayoutControls(int top, int width, bool measureOnly)
        {
            Size size = base.LayoutControls(top, width, measureOnly);

            if (!measureOnly)
            {
                int buttonHeight = EditRegionSize.Height - EditorLineButtonPadding * 2 - 1;
                _button.Location = new Point(EditRegionLocation.X + EditRegionSize.Width - buttonHeight - EditorLineButtonPadding, EditRegionLocation.Y + EditorLineButtonPadding + 1);
                _button.Size = new Size(buttonHeight, buttonHeight);
            }

            return size;
        }

        private void OnButtonClick(object sender, EventArgs e)
        {
            ActivateDropDown();
        }

        private void OnButtonGotFocus(object sender, EventArgs e)
        {
            if (!_button.Ellipsis)
            {
                Focus();
            }
        }

        private void OnListBoxKeyDown(object sender, KeyEventArgs e)
        {
            // Always respect the enter key and F4
            if (e.KeyData == Keys.Enter)
            {
                _ignoreNextSelectChange = false;
                CloseDropDown();
                e.Handled = true;
            }
            else
            {
                // Ignore selected index change events when the user is navigating via the keyboard
                _ignoreNextSelectChange = true;
            }
        }

        private void OnListBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            // If we're ignoring this selected index change, do nothing
            if (_ignoreNextSelectChange)
            {
                _ignoreNextSelectChange = false;
            }
            else
            {
                CloseDropDown();
            }
        }

        protected override void OnPropertyTaskItemUpdated(ToolTip toolTip, ref int currentTabIndex)
        {
            _editor = (UITypeEditor)PropertyDescriptor.GetEditor(typeof(UITypeEditor));

            base.OnPropertyTaskItemUpdated(toolTip, ref currentTabIndex);

            if (_editor is not null)
            {
                _button.Ellipsis = (_editor.GetEditStyle(TypeDescriptorContext) == UITypeEditorEditStyle.Modal);
                _hasSwatch = _editor.GetPaintValueSupported(TypeDescriptorContext);
            }
            else
            {
                _button.Ellipsis = false;
            }

            if (_button.Ellipsis)
            {
                EditControl.AccessibleRole = (IsReadOnly() ? AccessibleRole.StaticText : AccessibleRole.Text);
            }
            else
            {
                EditControl.AccessibleRole = (IsReadOnly() ? AccessibleRole.DropList : AccessibleRole.ComboBox);
            }

            _button.TabStop = _button.Ellipsis;
            _button.TabIndex = currentTabIndex++;
            _button.AccessibleRole = (_button.Ellipsis ? AccessibleRole.PushButton : AccessibleRole.ButtonDropDown);

            _button.AccessibleDescription = EditControl.AccessibleDescription;
            _button.AccessibleName = EditControl.AccessibleName;
        }

        protected override void OnReadOnlyTextBoxLabelClick(object sender, MouseEventArgs e)
        {
            base.OnReadOnlyTextBoxLabelClick(sender, e);

            if (e.Button == MouseButtons.Left)
            {
                if (ActionPanel.DropDownActive)
                {
                    _ignoreDropDownValue = true;
                    CloseDropDown();
                }
                else
                {
                    ActivateDropDown();
                }
            }
        }

        protected override void OnValueChanged()
        {
            base.OnValueChanged();

            _swatch = null;
            if (_hasSwatch)
            {
                ActionPanel.Invalidate(new Rectangle(EditRegionLocation, EditRegionSize), false);
            }
        }

        public override void PaintLine(Graphics g, int lineWidth, int lineHeight)
        {
            base.PaintLine(g, lineWidth, lineHeight);

            if (_hasSwatch)
            {
                if (_swatch is null)
                {
                    int width = EditRegionSize.Height - EditorLineSwatchPadding * 2;
                    int height = width - 1;
                    _swatch = new Bitmap(width, height);
                    Rectangle rect = new Rectangle(1, 1, width - 2, height - 2);
                    using (Graphics swatchGraphics = Graphics.FromImage(_swatch))
                    {
                        _editor.PaintValue(Value, swatchGraphics, rect);
                        swatchGraphics.DrawRectangle(SystemPens.ControlDark, new Rectangle(0, 0, width - 1, height - 1));
                    }
                }

                g.DrawImage(_swatch, new Point(EditRegionRelativeLocation.X + 2, EditorLineSwatchPadding + 5));
            }
        }

        protected internal override bool ProcessDialogKey(Keys keyData)
        {
            // Do this here rather than in OnKeyDown because if hierarchy is properly set,
            // VS is going to eat the F4 in PreProcessMessage, preventing it from ever
            // getting to an OnKeyDown on this control. Doing it here also allow to not
            // hook up to multiple events for each button.
            if (!_button.Focused && !_button.Ellipsis)
            {
                if ((keyData == (Keys.Alt | Keys.Down)) || (keyData == (Keys.Alt | Keys.Up)) || (keyData == Keys.F4))
                {
                    if (!ActionPanel.DropDownActive)
                    {
                        ActivateDropDown();
                    }
                    else
                    {
                        CloseDropDown();
                    }

                    return true;
                }

                // Not passing Alt key event to base class to prevent  closing 'Combobox Tasks window'
                else if ((keyData & Keys.Alt) == Keys.Alt)
                {
                    return true;
                }
            }

            return base.ProcessDialogKey(keyData);
        }

        private void ShowDropDown(Control hostedControl, Color borderColor)
        {
            hostedControl.Width = Math.Max(hostedControl.Width, EditRegionSize.Width - 2);

            _dropDownHolder = new DropDownHolder(hostedControl, ActionPanel, borderColor, ActionPanel.Font, this);

            if (ActionPanel.RightToLeft != RightToLeft.Yes)
            {
                Rectangle editorBounds = new Rectangle(Point.Empty, EditRegionSize);
                Size dropDownSize = _dropDownHolder.Size;
                Point editorLocation = ActionPanel.PointToScreen(EditRegionLocation);
                Rectangle rectScreen = Screen.FromRectangle(ActionPanel.RectangleToScreen(editorBounds)).WorkingArea;
                dropDownSize.Width = Math.Max(editorBounds.Width + 1, dropDownSize.Width);

                editorLocation.X = Math.Min(rectScreen.Right - dropDownSize.Width, // min = right screen edge clip
                    Math.Max(rectScreen.X, editorLocation.X + editorBounds.Right - dropDownSize.Width)); // max = left screen edge clip
                editorLocation.Y += editorBounds.Y;
                if (rectScreen.Bottom < (dropDownSize.Height + editorLocation.Y + editorBounds.Height))
                {
                    editorLocation.Y -= dropDownSize.Height + 1;
                }
                else
                {
                    editorLocation.Y += editorBounds.Height;
                }

                _dropDownHolder.Location = editorLocation;
            }
            else
            {
                _dropDownHolder.RightToLeft = ActionPanel.RightToLeft;

                Rectangle editorBounds = new Rectangle(Point.Empty, EditRegionSize);
                Size dropDownSize = _dropDownHolder.Size;
                Point editorLocation = ActionPanel.PointToScreen(EditRegionLocation);
                Rectangle rectScreen = Screen.FromRectangle(ActionPanel.RectangleToScreen(editorBounds)).WorkingArea;
                dropDownSize.Width = Math.Max(editorBounds.Width + 1, dropDownSize.Width);

                editorLocation.X = Math.Min(rectScreen.Right - dropDownSize.Width, // min = right screen edge clip
                    Math.Max(rectScreen.X, editorLocation.X - editorBounds.Width)); // max = left screen edge clip
                editorLocation.Y += editorBounds.Y;
                if (rectScreen.Bottom < (dropDownSize.Height + editorLocation.Y + editorBounds.Height))
                {
                    editorLocation.Y -= dropDownSize.Height + 1;
                }
                else
                {
                    editorLocation.Y += editorBounds.Height;
                }

                _dropDownHolder.Location = editorLocation;
            }

            ActionPanel.InMethodInvoke = true;
            ActionPanel.SetDropDownActive(true);
            try
            {
                _dropDownHolder.ShowDropDown(_button);
            }
            finally
            {
                _button.ResetMouseStates();
                ActionPanel.SetDropDownActive(false);
                ActionPanel.InMethodInvoke = false;
            }
        }

        #region IWindowsFormsEditorService implementation
        void IWindowsFormsEditorService.CloseDropDown()
        {
            CloseDropDown();
        }

        void IWindowsFormsEditorService.DropDownControl(Control control)
        {
            ShowDropDown(control, ActionPanel.BorderColor);
        }

        DialogResult IWindowsFormsEditorService.ShowDialog(Form dialog)
        {
            IUIService uiService = (IUIService)ServiceProvider.GetService(typeof(IUIService));
            if (uiService is not null)
            {
                return uiService.ShowDialog(dialog);
            }

            return dialog.ShowDialog();
        }
        #endregion

        #region IServiceProvider implementation
        object IServiceProvider.GetService(Type serviceType)
        {
            // Inject this class as the IWindowsFormsEditorService
            // so drop-down custom editors can work
            if (serviceType == typeof(IWindowsFormsEditorService))
            {
                return this;
            }

            return ServiceProvider.GetService(serviceType);
        }
        #endregion

        private class DropDownHolder : FlyoutDialog
        {
            private readonly EditorPropertyLine _parent;

            public DropDownHolder(Control hostedControl, Control parentControl, Color borderColor, Font font, EditorPropertyLine parent)
                : base(hostedControl, parentControl, borderColor, font)
            {
                _parent = parent;
                _parent.ActionPanel.SetDropDownActive(true);
            }

            protected override void OnClosed(EventArgs e)
            {
                base.OnClosed(e);
                _parent.ActionPanel.SetDropDownActive(false);
            }

            protected override bool ProcessDialogKey(Keys keyData)
            {
                if (keyData == Keys.Escape)
                {
                    // Indicates that the selection was aborted so we should ignore the value
                    _parent._ignoreDropDownValue = true;
                    Visible = false;
                    return true;
                }

                return base.ProcessDialogKey(keyData);
            }
        }
    }
}
