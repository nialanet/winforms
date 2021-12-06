﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using static Interop;

namespace System.Drawing.Design
{
    /// <summary>
    ///  Provides a font editor that is used to visually select and configure a Font object.
    /// </summary>
    [CLSCompliant(false)]
    public class FontEditor : UITypeEditor
    {
        private FontDialog _fontDialog;

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            // Even though we don't use the editor service this is historically what we did.
            if (!provider.TryGetService(out IWindowsFormsEditorService _))
            {
                return value;
            }

            _fontDialog ??= new FontDialog
            {
                ShowApply = false,
                ShowColor = false,
                AllowVerticalFonts = false
            };

            if (value is Font fontValue)
            {
                _fontDialog.Font = fontValue;
            }

            IntPtr hwndFocus = User32.GetFocus();
            try
            {
                if (_fontDialog.ShowDialog() == DialogResult.OK)
                {
                    return _fontDialog.Font;
                }
            }
            finally
            {
                if (hwndFocus != IntPtr.Zero)
                {
                    User32.SetFocus(hwndFocus);
                }
            }

            return value;
        }

        /// <inheritdoc />
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) => UITypeEditorEditStyle.Modal;
    }
}