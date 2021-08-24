
namespace WinForms_Sample__Basic_
{
    partial class BasicNaked
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.flyleaf1 = new FlyleafLib.Controls.Flyleaf();
            this.SuspendLayout();
            // 
            // flyleaf1
            // 
            this.flyleaf1.BackColor = System.Drawing.Color.Black;
            this.flyleaf1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.flyleaf1.Location = new System.Drawing.Point(0, 0);
            this.flyleaf1.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.flyleaf1.Name = "flyleaf1";
            this.flyleaf1.Size = new System.Drawing.Size(724, 446);
            this.flyleaf1.TabIndex = 0;
            // 
            // BasicNaked
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(724, 446);
            this.Controls.Add(this.flyleaf1);
            this.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.Name = "BasicNaked";
            this.Text = "Form1";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private FlyleafLib.Controls.Flyleaf flyleaf1;
    }
}

