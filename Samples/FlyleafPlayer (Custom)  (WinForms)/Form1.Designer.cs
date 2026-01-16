
namespace FlyleafPlayer__Custom_
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            flyleaf1 = new FlyleafLib.Controls.WinForms.FlyleafHost();
            flyleaf2 = new FlyleafLib.Controls.WinForms.FlyleafHost();
            btnSwap = new System.Windows.Forms.Button();
            label1 = new System.Windows.Forms.Label();
            label2 = new System.Windows.Forms.Label();
            label3 = new System.Windows.Forms.Label();
            label4 = new System.Windows.Forms.Label();
            label5 = new System.Windows.Forms.Label();
            label6 = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // flyleaf1
            // 
            flyleaf1.AllowDrop = true;
            flyleaf1.BackColor = System.Drawing.Color.Black;
            flyleaf1.DragMove = true;
            flyleaf1.IsFullScreen = false;
            flyleaf1.KeyBindings = true;
            flyleaf1.Location = new System.Drawing.Point(24, 52);
            flyleaf1.Name = "flyleaf1";
            flyleaf1.OpenOnDrop = false;
            flyleaf1.PanMoveOnCtrl = true;
            flyleaf1.PanRotateOnShiftWheel = true;
            flyleaf1.PanZoomOnCtrlWheel = true;
            flyleaf1.Player = null;
            flyleaf1.Size = new System.Drawing.Size(333, 250);
            flyleaf1.SwapDragEnterOnShift = true;
            flyleaf1.SwapOnDrop = true;
            flyleaf1.TabIndex = 0;
            flyleaf1.ToggleFullScreenOnDoubleClick = true;
            // 
            // flyleaf2
            // 
            flyleaf2.AllowDrop = true;
            flyleaf2.BackColor = System.Drawing.Color.Black;
            flyleaf2.DragMove = true;
            flyleaf2.IsFullScreen = false;
            flyleaf2.KeyBindings = true;
            flyleaf2.Location = new System.Drawing.Point(405, 52);
            flyleaf2.Name = "flyleaf2";
            flyleaf2.OpenOnDrop = false;
            flyleaf2.PanMoveOnCtrl = true;
            flyleaf2.PanRotateOnShiftWheel = true;
            flyleaf2.PanZoomOnCtrlWheel = true;
            flyleaf2.Player = null;
            flyleaf2.Size = new System.Drawing.Size(333, 250);
            flyleaf2.SwapDragEnterOnShift = true;
            flyleaf2.SwapOnDrop = true;
            flyleaf2.TabIndex = 1;
            flyleaf2.ToggleFullScreenOnDoubleClick = true;
            // 
            // btnSwap
            // 
            btnSwap.Location = new System.Drawing.Point(347, 352);
            btnSwap.Name = "btnSwap";
            btnSwap.Size = new System.Drawing.Size(75, 23);
            btnSwap.TabIndex = 2;
            btnSwap.Text = "Swap";
            btnSwap.UseVisualStyleBackColor = true;
            btnSwap.Click += btnSwap_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(85, 305);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(38, 15);
            label1.TabIndex = 3;
            label1.Text = "label1";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(234, 305);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(38, 15);
            label2.TabIndex = 4;
            label2.Text = "label2";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(24, 305);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(55, 15);
            label3.TabIndex = 5;
            label3.Text = "CurTime:";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new System.Drawing.Point(186, 305);
            label4.Name = "label4";
            label4.Size = new System.Drawing.Size(42, 15);
            label4.TabIndex = 6;
            label4.Text = "Buffer:";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new System.Drawing.Point(24, 34);
            label5.Name = "label5";
            label5.Size = new System.Drawing.Size(39, 15);
            label5.TabIndex = 7;
            label5.Text = "Status";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new System.Drawing.Point(69, 34);
            label6.Name = "label6";
            label6.Size = new System.Drawing.Size(38, 15);
            label6.TabIndex = 8;
            label6.Text = "status";
            // 
            // Form1
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(800, 450);
            Controls.Add(label6);
            Controls.Add(label5);
            Controls.Add(label4);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(btnSwap);
            Controls.Add(flyleaf2);
            Controls.Add(flyleaf1);
            Name = "Form1";
            Text = "Flyleaf Player (Custom)";
            Load += Form1_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private FlyleafLib.Controls.WinForms.FlyleafHost flyleaf1;
        private FlyleafLib.Controls.WinForms.FlyleafHost flyleaf2;
        private System.Windows.Forms.Button btnSwap;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label6;
    }
}

