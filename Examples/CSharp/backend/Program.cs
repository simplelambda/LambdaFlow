using System;
using System.Globalization;

LambdaFlow.Receive<object, PingResponse>("backend.ping", _ => new PingResponse("pong"));

LambdaFlow.Receive<TextRequest, TextResponse>("uppercase", req => new TextResponse(req.Text.ToUpperInvariant()));
LambdaFlow.Receive<TextRequest, TextResponse>("lowercase", req => new TextResponse(req.Text.ToLowerInvariant()));
LambdaFlow.Receive<TextRequest, TextResponse>("reverse",   req => new TextResponse(Reverse(req.Text)));
LambdaFlow.Receive<TextRequest, TextResponse>("charcount", req => new TextResponse(req.Text.Length.ToString()));
LambdaFlow.Receive<TextRequest, TextResponse>("wordcount", req => new TextResponse(WordCount(req.Text).ToString()));
LambdaFlow.Receive<TextRequest, TextStatsResponse>("textstats", req => BuildTextStats(req.Text));

LambdaFlow.Receive<NumberConvertRequest, TextResponse>("numberconverter", req => new TextResponse(ConvertNumber(req.Mode, req.Value)));

// Typed-object demo: a Dog instance round-trips between front and back.
LambdaFlow.Receive<Dog, LambdaFlow.OntologyEntity<DogDescription>>("describeDog", dog =>
    LambdaFlow.Entity("animals.dogDescription", new DogDescription(
        Greeting:      $"Hi! I'm {dog.Name}, a {dog.Age}-year-old {dog.Breed}.",
        AgeInDogYears: dog.Age * 7,
        IsPuppy:       dog.Age < 1
    ))
);

LambdaFlow.Run();


static string Reverse(string text) {
    var chars = text.ToCharArray();
    Array.Reverse(chars);
    return new string(chars);
}

static int WordCount(string text) =>
    text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

static TextStatsResponse BuildTextStats(string text) {
    var normalized = NormalizeLettersAndDigits(text);
    return new TextStatsResponse(
        Characters:        text.Length,
        CharactersNoSpaces: CountNonWhitespace(text),
        Words:             WordCount(text),
        Lines:             LineCount(text),
        IsPalindrome:      IsPalindrome(normalized),
        Normalized:        normalized
    );
}

static int CountNonWhitespace(string text) {
    var count = 0;
    foreach (var ch in text) {
        if (!char.IsWhiteSpace(ch)) count++;
    }
    return count;
}

static int LineCount(string text) {
    if (string.IsNullOrEmpty(text)) return 0;
    return text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Length;
}

static string NormalizeLettersAndDigits(string text) {
    if (string.IsNullOrEmpty(text)) return string.Empty;

    var buffer = new char[text.Length];
    var index  = 0;
    foreach (var ch in text) {
        if (!char.IsLetterOrDigit(ch)) continue;
        buffer[index++] = char.ToLowerInvariant(ch);
    }
    return new string(buffer, 0, index);
}

static bool IsPalindrome(string normalized) {
    if (string.IsNullOrEmpty(normalized)) return false;

    var left  = 0;
    var right = normalized.Length - 1;
    while (left < right) {
        if (normalized[left] != normalized[right]) return false;
        left++;
        right--;
    }
    return true;
}

static string ConvertNumber(string mode, string value) => mode switch {
    "dec2hex" => int.TryParse(value, out var d1) ? d1.ToString("X")              : "Invalid decimal number",
    "hex2dec" => int.TryParse(value, NumberStyles.HexNumber, null, out var d2) ? d2.ToString() : "Invalid hexadecimal number",
    "dec2bin" => int.TryParse(value, out var d3) ? Convert.ToString(d3, 2)       : "Invalid decimal number",
    "bin2dec" => TryParseBinary(value, out var d4) ? d4.ToString()               : "Invalid binary number",
    _         => "Invalid mode"
};

static bool TryParseBinary(string value, out int result) {
    try { result = Convert.ToInt32(value, 2); return true; }
    catch { result = 0; return false; }
}


public record TextRequest(string Text);
public record TextResponse(string Text);
public record PingResponse(string Status);
public record TextStatsResponse(int Characters, int CharactersNoSpaces, int Words, int Lines, bool IsPalindrome, string Normalized);
public record NumberConvertRequest(string Mode, string Value);
public record Dog(string Name, int Age, string Breed);
public record DogDescription(string Greeting, int AgeInDogYears, bool IsPuppy);
