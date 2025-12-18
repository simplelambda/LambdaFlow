using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;

class Program
{

    class Operation
    {
        [JsonPropertyName("type")]
        public string Name { get; set; }

        [JsonPropertyName("text")]
        public string Value { get; set; }
    }

    static void Main()
    {
        string text;
        while ((text = Console.ReadLine()) != null){
            Console.WriteLine("__ACK__");

            Operation op;

            try{
                op = JsonSerializer.Deserialize<Operation>(text) ?? throw new Exception("Embedded config.json malformed.");
            }
            catch (Exception ex){
                Console.Error.WriteLine($"Error deserializing json: {ex.Message}");
                continue;
            }

            op = op.Name.ToLowerInvariant() switch {
                "uppercase"       => ToUpper(op.Value),
                "lowercase"        => ToLower(op.Value),
                "charcount"       => CharCount(op.Value),
                "wordcount"       => WordCount(op.Value),
                "reverse"         => Reversee(op.Value),
                "numberconverter" => NumberConverter(op.Value),
                _ => null,
            };

            Console.WriteLine(JsonSerializer.Serialize<Operation>(op));
        }
    }

    private static Operation CharCount(string text) => new Operation { Name = "charcount", Value = text.Length.ToString() };
    private static Operation ToUpper(string text) => new Operation { Name = "uppercase", Value = text.ToUpperInvariant() };
    private static Operation ToLower(string text) => new Operation { Name = "lowercase", Value = text.ToLowerInvariant() };
    private static Operation WordCount(string text) => new Operation { Name = "wordcount", Value = text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length.ToString() };
    private static Operation Reversee(string text) {
        var textArray = text.ToCharArray();
        Array.Reverse(textArray);
        var newText = new string(textArray);

        return new Operation { Name = "reverse", Value = newText };
    }
    private static Operation NumberConverter(string text) {

        var split = text.Split(',');

        var mode = split[0];
        var numerText = split[1].ToString();

        return mode.ToLowerInvariant() switch {
            "dec2hex" => dec2hex(numerText),
            "hex2dec" => hex2dec(numerText),
            "dec2bin" => dec2bin(numerText),
            "bin2dec" => bin2dec(numerText),
            _         => new Operation { Name = "numberconverter", Value = "Invalid mode" }
        };
    }


    private static Operation dec2hex(string text) {
        if (int.TryParse(text, out int number)) {
            return new Operation { Name = "numberconverter", Value = number.ToString("X") };
        }
        return new Operation { Name = "numberconverter", Value = "Invalid decimal number" };
    }

    private static Operation hex2dec(string text) {
        if (int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out int number)) {
            return new Operation { Name = "numberconverter", Value = number.ToString() };
        }
        return new Operation { Name = "numberconverter", Value = "Invalid hexadecimal number" };
    }

    private static Operation dec2bin(string text) {
        if (int.TryParse(text, out int number)) {
            return new Operation { Name = "numberconverter", Value = Convert.ToString(number, 2) };
        }
        return new Operation { Name = "numberconverter", Value = "Invalid decimal number" };
    }

    private static Operation bin2dec(string text) {
        if (int.TryParse(text, System.Globalization.NumberStyles.AllowBinarySpecifier, null, out int number)) {
            return new Operation { Name = "numberconverter", Value = number.ToString() };
        }
        return new Operation { Name = "numberconverter", Value = "Invalid binary number" };
    }
}