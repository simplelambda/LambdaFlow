package example;

import lambdaflow.LambdaFlow;

public class Backend {

    // -- DTOs -----------------------------------------------------------

    public static class TextRequest {
        public String text;
    }

    public static class TextResponse {
        public final String text;
        public TextResponse(String text) { this.text = text; }
    }

    public static class PingResponse {
        public final String status;
        public PingResponse(String status) { this.status = status; }
    }

    public static class NumberConvertRequest {
        public String mode;
        public String value;
    }

    public static class TextStatsResponse {
        public final int     characters;
        public final int     charactersNoSpaces;
        public final int     words;
        public final int     lines;
        public final boolean isPalindrome;
        public final String  normalized;

        public TextStatsResponse(int characters, int charactersNoSpaces, int words, int lines, boolean isPalindrome, String normalized) {
            this.characters        = characters;
            this.charactersNoSpaces = charactersNoSpaces;
            this.words             = words;
            this.lines             = lines;
            this.isPalindrome      = isPalindrome;
            this.normalized        = normalized;
        }
    }

    public static class Dog {
        public String name;
        public int    age;
        public String breed;
    }

    public static class DogDescription {
        public final String  greeting;
        public final int     ageInDogYears;
        public final boolean isPuppy;
        public DogDescription(String g, int a, boolean p) {
            this.greeting = g; this.ageInDogYears = a; this.isPuppy = p;
        }
    }

    // -- Handlers -------------------------------------------------------

    public static void main(String[] args) {
        LambdaFlow.receive("backend.ping", Object.class, req -> new PingResponse("pong"));

        LambdaFlow.receive("uppercase", TextRequest.class, req -> new TextResponse(req.text.toUpperCase()));
        LambdaFlow.receive("lowercase", TextRequest.class, req -> new TextResponse(req.text.toLowerCase()));
        LambdaFlow.receive("reverse",   TextRequest.class, req -> new TextResponse(new StringBuilder(req.text).reverse().toString()));
        LambdaFlow.receive("charcount", TextRequest.class, req -> new TextResponse(String.valueOf(req.text.length())));
        LambdaFlow.receive("wordcount", TextRequest.class, req -> new TextResponse(String.valueOf(wordCount(req.text))));
        LambdaFlow.receive("textstats", TextRequest.class, req -> textStats(req.text));

        LambdaFlow.receive("numberconverter", NumberConvertRequest.class, req -> new TextResponse(convert(req.mode, req.value)));

        LambdaFlow.receive("describeDog", Dog.class, dog -> LambdaFlow.entity(
            "animals.dogDescription",
            new DogDescription(
                "Hi! I'm " + dog.name + ", a " + dog.age + "-year-old " + dog.breed + ".",
                dog.age * 7,
                dog.age < 1
            )
        ));

        LambdaFlow.run();
    }

    // -- Helpers --------------------------------------------------------

    private static int wordCount(String text) {
        if (text == null) return 0;
        String trimmed = text.trim();
        if (trimmed.isEmpty()) return 0;
        return trimmed.split("\\s+").length;
    }

    private static TextStatsResponse textStats(String text) {
        String safe = text == null ? "" : text;
        String normalized = normalizeLettersAndDigits(safe);
        return new TextStatsResponse(
            safe.length(),
            countNonWhitespace(safe),
            wordCount(safe),
            lineCount(safe),
            isPalindrome(normalized),
            normalized
        );
    }

    private static int countNonWhitespace(String text) {
        int count = 0;
        for (int i = 0; i < text.length(); i++) {
            if (!Character.isWhitespace(text.charAt(i))) count++;
        }
        return count;
    }

    private static int lineCount(String text) {
        if (text.isEmpty()) return 0;
        return text.replace("\r\n", "\n").split("\n", -1).length;
    }

    private static String normalizeLettersAndDigits(String text) {
        StringBuilder builder = new StringBuilder(text.length());
        for (int i = 0; i < text.length(); i++) {
            char ch = text.charAt(i);
            if (!Character.isLetterOrDigit(ch)) continue;
            builder.append(Character.toLowerCase(ch));
        }
        return builder.toString();
    }

    private static boolean isPalindrome(String normalized) {
        if (normalized.isEmpty()) return false;

        int left = 0;
        int right = normalized.length() - 1;
        while (left < right) {
            if (normalized.charAt(left) != normalized.charAt(right)) return false;
            left++;
            right--;
        }
        return true;
    }

    private static String convert(String mode, String value) {
        try {
            switch (mode) {
                case "dec2hex": return Integer.toHexString(Integer.parseInt(value)).toUpperCase();
                case "hex2dec": return String.valueOf(Integer.parseInt(value, 16));
                case "dec2bin": return Integer.toBinaryString(Integer.parseInt(value));
                case "bin2dec": return String.valueOf(Integer.parseInt(value, 2));
                default:        return "Invalid mode";
            }
        } catch (NumberFormatException ex) {
            return "Invalid " + mode.split("2")[0] + " number";
        }
    }
}
