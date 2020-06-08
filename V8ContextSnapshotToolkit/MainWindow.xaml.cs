using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace V8ContextSnapshotToolkit
{
    class ConsolePrinter
    {
        private ListBox ConsoleBox;

        public ConsolePrinter(ListBox consoleBox)
        {
            ConsoleBox = consoleBox;
        }

        public void PrintItem(string message, SolidColorBrush color)
        {
            ListBoxItem messageItem = new ListBoxItem();
            messageItem.Content = $"[{DateTime.Now.ToString("hh:mm:ss")}] {message}";
            messageItem.Foreground = color;

            ContextMenu menu = new ContextMenu();
            MenuItem copyToClipboard = new MenuItem();
            copyToClipboard.Header = "Copy to clipboard";
            copyToClipboard.Click += new RoutedEventHandler(CopyToClipboard);
            menu.Items.Add(copyToClipboard);
            messageItem.ContextMenu = menu;

            ConsoleBox.Items.Add(messageItem);

            Border border = (Border)VisualTreeHelper.GetChild(ConsoleBox, 0);
            ScrollViewer scrollViewer = (ScrollViewer)VisualTreeHelper.GetChild(border, 0);
            scrollViewer.ScrollToBottom();
        }

        public void Print(string message)
        {
            PrintItem(message, Brushes.Black);
        }

        public void PrintWarning(string message)
        {
            PrintItem(message, Brushes.Orange);
        }

        public void PrintError(string message)
        {
            PrintItem(message, Brushes.Red);
        }

        public void PrintSuccess(string message)
        {
            PrintItem(message, Brushes.Green);
        }

        private void CopyToClipboard(object sender, RoutedEventArgs e)
        {
            List<string> selectedMessages = new List<string>();

            for (int index = 0; index < ConsoleBox.Items.Count; index++)
            {
                if (((ListBoxItem)ConsoleBox.Items[index]).IsSelected)
                {
                    selectedMessages.Add((string)((ListBoxItem)ConsoleBox.Items[index]).Content);
                }
            }

            Clipboard.SetText(String.Join("\n", selectedMessages));
        }
    }

    class FletcherChecksum
    {
        public UInt32 ChecksumA;
        public UInt32 ChecksumB;

        private string ChecksumToHex(UInt32 checksum)
        {
            return BitConverter.ToString(BitConverter.GetBytes(checksum)).Replace("-", "");
        }

        public string GetChecksum()
        {
            return ChecksumToHex(ChecksumA) + ChecksumToHex(ChecksumB);
        }
    }

    class V8ContextSnapshotHeader
    {
        public UInt32 NumberOfContexts;
        public UInt32 Rehashability;
        public FletcherChecksum Checksum;
        public string VersionString;
        public UInt32 OffsetToReadOnly;
        public UInt32 OffsetToContext0;
        public UInt32 OffsetToContext1;
    }

    public partial class MainWindow : Window
    {
        private ConsolePrinter Printer;

        public MainWindow()
        {
            InitializeComponent();
            Printer = new ConsolePrinter(ConsoleBox);
        }

        private void SelectFile(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "V8 Context Snapshots (.bin)|*.bin";

            bool result = (bool)dialog.ShowDialog();

            if (result)
            {
                FileSelection.Text = dialog.FileName;
            }
        }

        private bool IsSnapshotValid()
        {
            if (FileSelection.Text == "")
            {
                Printer.PrintError("You must first select a valid V8 context snapshot!");
                return false;
            }
            else
            {
                return true;
            }
        }

        private FletcherChecksum CalculateChecksum(BinaryReader reader)
        {
            // We save this so the stream can reset its position.
            long streamPosition = reader.BaseStream.Position;

            // We skip over the first parts of the header (these aren't included in checksum).
            reader.BaseStream.Seek(16, SeekOrigin.Begin);

            // Now we can begin calculating the checksum.
            UInt64 checksumA = 1;
            UInt64 checksumB = 0;

            try
            {
                while (true)
                {
                    checksumA += reader.ReadUInt64();
                    checksumB += checksumA;
                }
            }
            catch (EndOfStreamException) { }

            checksumA ^= checksumA >> 32;
            checksumB ^= checksumB >> 32;

            // We seek back to where the stream was originally
            reader.BaseStream.Seek(streamPosition, SeekOrigin.Begin);

            return new FletcherChecksum
            {
                ChecksumA = (UInt32)checksumA,
                ChecksumB = (UInt32)checksumB
            };
        }

        private V8ContextSnapshotHeader ParseHeader(BinaryReader reader)
        {
            // We save this so the stream can reset its position.
            long streamPosition = reader.BaseStream.Position;

            V8ContextSnapshotHeader header = new V8ContextSnapshotHeader
            {
                NumberOfContexts = reader.ReadUInt32(),
                Rehashability = reader.ReadUInt32(),
                Checksum = new FletcherChecksum
                {
                    ChecksumA = reader.ReadUInt32(),
                    ChecksumB = reader.ReadUInt32()
                },
                VersionString = new string(reader.ReadChars(64)).Trim('\u0000'),
                OffsetToReadOnly = reader.ReadUInt32(),
                OffsetToContext0 = reader.ReadUInt32(),
                OffsetToContext1 = reader.ReadUInt32()
            };

            // We seek back to where the stream was originally
            reader.BaseStream.Seek(streamPosition, SeekOrigin.Begin);

            return header;
        }

        private void PrintHeader(object sender, RoutedEventArgs e)
        {
            if (!IsSnapshotValid())
                return;

            V8ContextSnapshotHeader header;

            using (FileStream stream = File.OpenRead(FileSelection.Text))
            {
                BinaryReader reader = new BinaryReader(stream);
                header = ParseHeader(reader);
            }

            Printer.Print($"Number of contexts: {header.NumberOfContexts}");
            Printer.Print($"Rehashability: {header.Rehashability}");
            Printer.Print($"Checksum: {header.Checksum.GetChecksum()}");
            Printer.Print($"Version string: {header.VersionString}");
            Printer.Print($"Offset to readonly: {header.OffsetToReadOnly}");
            Printer.Print($"Offset to context 0: {header.OffsetToContext0}");
            Printer.Print($"Offset to context 1: {header.OffsetToContext1}");
        }

        private void CompareChecksums(object sender, RoutedEventArgs e)
        {
            if (!IsSnapshotValid())
                return;

            V8ContextSnapshotHeader header;
            FletcherChecksum checksum;

            using (FileStream stream = File.OpenRead(FileSelection.Text))
            {
                BinaryReader reader = new BinaryReader(stream);
                header = ParseHeader(reader);
                checksum = CalculateChecksum(reader);
            }

            if (checksum.GetChecksum() == header.Checksum.GetChecksum())
            {
                Printer.PrintSuccess($"Checksum is a match ({checksum.GetChecksum()} == {header.Checksum.GetChecksum()})");
            }
            else
            {
                Printer.PrintError($"Checksum does not match ({checksum.GetChecksum()} != {header.Checksum.GetChecksum()})");
            }
        }

        private void ResetChecksums(object sender, RoutedEventArgs e)
        {
            if (!IsSnapshotValid())
                return;

            FletcherChecksum checksum;

            using (FileStream stream = File.Open(FileSelection.Text, FileMode.Open, FileAccess.ReadWrite))
            {
                BinaryReader reader = new BinaryReader(stream);
                checksum = CalculateChecksum(reader);

                BinaryWriter writer = new BinaryWriter(stream);
                writer.BaseStream.Seek(8, SeekOrigin.Begin);
                writer.Write(checksum.ChecksumA);
                writer.Write(checksum.ChecksumB);
            }

            Printer.PrintSuccess($"The checksum was reset successfully ({checksum.GetChecksum()})");
        }
    }
}
