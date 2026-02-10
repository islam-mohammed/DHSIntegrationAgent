using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DHSIntegrationAgent.App.UI.Views
{
    /// <summary>
    /// Interaction logic for DashboardView.xaml
    /// </summary>
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
           
            var values = new int[] { 10, 25, 18, 30 };
            var labels = new string[] { "Jan", "Feb", "Mar", "Apr" };

            
            var series = new ColumnSeries<int>
            {
                Values = values,
                Fill = new SolidColorPaint(SKColors.Teal),  
                MaxBarWidth = 50
            };

            // Chart
            var chart = new CartesianChart
            {
                Series = new ISeries[] { series },
                XAxes = new Axis[]
                {
                    new Axis { Labels = labels }
                },
                YAxes = new Axis[]
                {
                    new Axis()
                }
            };

             
            CardChart.Content = chart;
            // ----------- Pie Chart -----------------
            //  Pie Chart
            var pieSeries = new ISeries[]
            {
               new PieSeries<double> { Values = new double[] { 60 }, Name = "Staged", Fill = new SolidColorPaint(SKColor.Parse("#AE9EC9")), Stroke = new SolidColorPaint(SKColors.White)   },
                new PieSeries<double> { Values = new double[] { 20 }, Name = "Completed", Fill = new SolidColorPaint(SKColor.Parse("#4CA6A6")), Stroke = new SolidColorPaint(SKColors.White) },
                new PieSeries<double> { Values = new double[] { 10 }, Name = "Enqueued", Fill = new SolidColorPaint(SKColor.Parse("#3f8fc1")), Stroke = new SolidColorPaint(SKColors.White) }, 
                new PieSeries<double> { Values = new double[] { 10 }, Name = "Failed", Fill = new SolidColorPaint(SKColor.Parse("#EC7063")), Stroke = new SolidColorPaint(SKColors.White) }   
            };

           
            var pieChart = new PieChart
            {
                Series = pieSeries,
                LegendPosition = LiveChartsCore.Measure.LegendPosition.Right,  
            };

           
            CardPieChart.Content = pieChart;
        }
    }
}
