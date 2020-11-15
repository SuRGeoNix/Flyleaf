using System;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SuRGeoNix.Flyleaf.Controls
{
    public partial class FrmLanguages : Form
    {
        public string[] Languages { get; set; }

        public FrmLanguages()
        {
            InitializeComponent();

            FormClosing     += FrmLanguages_FormClosing;
            VisibleChanged  += FrmLanguages_VisibleChanged;
        }

        private void FrmLanguages_VisibleChanged(object sender, EventArgs e)
        {
            if (!Visible) return;

            lstSelected.Items.Clear();

            foreach (string strLang in Languages)
                lstSelected.Items.Add(Language.Get(strLang));

            textBox1_TextChanged(null, null);
        }
        private void FrmLanguages_FormClosing(object sender, FormClosingEventArgs e)
        {
            Languages = new string[lstSelected.Items.Count];

            for (int i=0; i<lstSelected.Items.Count; i++)
                Languages[i] = lstSelected.Items[i].ToString();

            e.Cancel = true;
            Hide();

        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            lstAvailable.BeginUpdate();
            lstAvailable.Items.Clear();

            if (textBox1.Text.Trim() == "")
            {    
                lstAvailable.Items.AddRange(Language.Languages.ToArray());
            }
            else
            {
                foreach (Language lang in Language.Languages)
                {
                    if (Regex.IsMatch(lang.LanguageName, textBox1.Text, RegexOptions.IgnoreCase))
                        lstAvailable.Items.Add(lang);
                }
            }

            foreach (var t1 in lstSelected.Items)
                lstAvailable.Items.Remove(t1);

            lstAvailable.EndUpdate();
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            foreach (var t1 in lstAvailable.SelectedItems)
                lstSelected.Items.Add(t1);

            foreach (var t1 in lstSelected.Items)
                lstAvailable.Items.Remove(t1);
        }
        private void btnRemove_Click(object sender, EventArgs e)
        {
            foreach (var t1 in lstSelected.SelectedItems)
                lstAvailable.Items.Add(t1);

            foreach (var t1 in lstAvailable.Items)
                lstSelected.Items.Remove(t1);
        }
    }
}
