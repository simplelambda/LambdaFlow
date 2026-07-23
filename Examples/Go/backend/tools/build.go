package main

import (
	"flag"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

func main() {
	target := flag.String("target", "", "win-x64, win-arm64, linux-x64, or linux-arm64")
	flag.Parse()

	parts := strings.Split(*target, "-")
	if len(parts) != 2 || (parts[0] != "win" && parts[0] != "linux") ||
		(parts[1] != "x64" && parts[1] != "arm64") {
		fmt.Fprintln(os.Stderr, "Invalid --target. Use win-x64, win-arm64, linux-x64, or linux-arm64.")
		os.Exit(2)
	}

	goos := parts[0]
	if goos == "win" {
		goos = "windows"
	}
	goarch := map[string]string{"x64": "amd64", "arm64": "arm64"}[parts[1]]

	name := "Backend"
	if goos == "windows" {
		name += ".exe"
	}
	outputDir := filepath.Join("bin", *target)
	if err := os.RemoveAll(outputDir); err != nil {
		fail(err)
	}
	if err := os.MkdirAll(outputDir, 0o755); err != nil {
		fail(err)
	}

	command := exec.Command("go", "build", "-trimpath", "-ldflags=-s -w", "-o", filepath.Join(outputDir, name), "backend.go")
	command.Env = append(os.Environ(), "CGO_ENABLED=0", "GOOS="+goos, "GOARCH="+goarch)
	command.Stdout = os.Stdout
	command.Stderr = os.Stderr
	if err := command.Run(); err != nil {
		fail(err)
	}
}

func fail(err error) {
	fmt.Fprintln(os.Stderr, err)
	os.Exit(1)
}
