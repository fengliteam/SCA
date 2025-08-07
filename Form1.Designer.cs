namespace sca
{
    partial class Main : Form // 确保 Main 类继承自 Form
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Main));
            尝试破解 = new Button();
            button1 = new Button();
            label1 = new Label();
            button2 = new Button();
            button3 = new Button();
            button4 = new Button();
            button5 = new Button();
            button6 = new Button();
            button7 = new Button();
            button8 = new Button();
            SuspendLayout();
            // 
            // 尝试破解
            // 
            尝试破解.Location = new Point(170, 131);
            尝试破解.Name = "尝试破解";
            尝试破解.Size = new Size(188, 108);
            尝试破解.TabIndex = 1;
            尝试破解.Text = "尝试破解";
            尝试破解.UseVisualStyleBackColor = true;
            尝试破解.Click += 尝试破解_Click;
            // 
            // button1
            // 
            button1.Location = new Point(12, 377);
            button1.Name = "button1";
            button1.Size = new Size(100, 23);
            button1.TabIndex = 2;
            button1.Text = "重启资源管理器";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(170, 316);
            label1.Name = "label1";
            label1.Size = new Size(188, 17);
            label1.TabIndex = 3;
            label1.Text = "以下为测试内容，稳定性无法保证";
            // 
            // button2
            // 
            button2.Location = new Point(118, 377);
            button2.Name = "button2";
            button2.Size = new Size(99, 23);
            button2.TabIndex = 4;
            button2.Text = "恢复系统组件";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            // 
            // button3
            // 
            button3.Location = new Point(223, 377);
            button3.Name = "button3";
            button3.Size = new Size(127, 23);
            button3.TabIndex = 5;
            button3.Text = "恢复 USB 设备访问";
            button3.UseVisualStyleBackColor = true;
            button3.Click += button3_Click;
            // 
            // button4
            // 
            button4.Location = new Point(356, 377);
            button4.Name = "button4";
            button4.Size = new Size(221, 23);
            button4.TabIndex = 6;
            button4.Text = "重置Google和Edge浏览器管理策略";
            button4.UseVisualStyleBackColor = true;
            button4.Click += button4_Click;
            // 
            // button5
            // 
            button5.Location = new Point(12, 348);
            button5.Name = "button5";
            button5.Size = new Size(261, 23);
            button5.TabIndex = 7;
            button5.Text = "禁止程序运行";
            button5.UseVisualStyleBackColor = true;
            button5.Click += button5_Click;
            // 
            // button6
            // 
            button6.Location = new Point(279, 348);
            button6.Name = "button6";
            button6.Size = new Size(298, 23);
            button6.TabIndex = 8;
            button6.Text = "重新允许运行";
            button6.UseVisualStyleBackColor = true;
            button6.Click += button6_Click;
            // 
            // button7
            // 
            button7.Location = new Point(502, 12);
            button7.Name = "button7";
            button7.Size = new Size(75, 23);
            button7.TabIndex = 9;
            button7.Text = "关于";
            button7.UseVisualStyleBackColor = true;
            button7.Click += button7_Click;
            // 
            // button8
            // 
            button8.Location = new Point(421, 12);
            button8.Name = "button8";
            button8.Size = new Size(75, 23);
            button8.TabIndex = 10;
            button8.Text = "检查更新";
            button8.UseVisualStyleBackColor = true;
            button8.Click += button8_Click;
            // 
            // Main
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoValidate = AutoValidate.EnablePreventFocusChange;
            ClientSize = new Size(589, 412);
            Controls.Add(button8);
            Controls.Add(button7);
            Controls.Add(button6);
            Controls.Add(button5);
            Controls.Add(button4);
            Controls.Add(button3);
            Controls.Add(button2);
            Controls.Add(label1);
            Controls.Add(button1);
            Controls.Add(尝试破解);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            HelpButton = true;
            Icon = (Icon)resources.GetObject("$this.Icon");
            ImeMode = ImeMode.Off;
            MaximizeBox = false;
            Name = "Main";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Button 尝试破解;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.Button button4;
        private System.Windows.Forms.Button button5;
        private System.Windows.Forms.Button button6;
        private Button button7;
        private Button button8;
    }
}