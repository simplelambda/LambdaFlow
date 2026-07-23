package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"strings"
	"sync"
)

type envelope struct {
	Kind    string          `json:"kind"`
	ID      string          `json:"id,omitempty"`
	OK      *bool           `json:"ok,omitempty"`
	Payload json.RawMessage `json:"payload,omitempty"`
	Error   *protocolError  `json:"error,omitempty"`
}

type protocolError struct {
	Code    string `json:"code,omitempty"`
	Message string `json:"message"`
}

var outputMu sync.Mutex

func main() {
	scanner := bufio.NewScanner(os.Stdin)
	scanner.Buffer(make([]byte, 64*1024), 16*1024*1024)
	var handlers sync.WaitGroup

	for scanner.Scan() {
		line := append([]byte(nil), scanner.Bytes()...)
		if len(strings.TrimSpace(string(line))) == 0 {
			continue
		}
		handlers.Add(1)
		go func() {
			defer handlers.Done()
			dispatch(line)
		}()
	}
	handlers.Wait()

	if err := scanner.Err(); err != nil {
		fmt.Fprintf(os.Stderr, "LambdaFlow input failed: %v\n", err)
	}
}

func dispatch(raw []byte) {
	var request envelope
	if err := json.Unmarshal(raw, &request); err != nil || strings.TrimSpace(request.Kind) == "" {
		fmt.Fprintln(os.Stderr, "Invalid LambdaFlow envelope: kind must be a non-empty string")
		return
	}

	var payload any
	var handlerErr *protocolError

	switch request.Kind {
	case "backend.ping":
		payload = map[string]string{"status": "pong", "runtime": "go"}
	case "uppercase":
		var input struct {
			Text string `json:"text"`
		}
		if err := json.Unmarshal(request.Payload, &input); err != nil {
			handlerErr = &protocolError{Code: "INVALID_INPUT", Message: "payload.text is required"}
		} else {
			payload = map[string]string{"text": strings.ToUpper(input.Text)}
		}
	default:
		handlerErr = &protocolError{Code: "HANDLER_NOT_FOUND", Message: "No handler for " + request.Kind}
	}

	if request.ID == "" {
		return
	}

	ok := handlerErr == nil
	response := envelope{
		Kind:  request.Kind + ".result",
		ID:    request.ID,
		OK:    &ok,
		Error: handlerErr,
	}
	if ok {
		encoded, err := json.Marshal(payload)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Failed to encode response: %v\n", err)
			return
		}
		response.Payload = encoded
	}
	writeEnvelope(response)
}

func writeEnvelope(response envelope) {
	encoded, err := json.Marshal(response)
	if err != nil {
		fmt.Fprintf(os.Stderr, "Failed to encode envelope: %v\n", err)
		return
	}

	outputMu.Lock()
	defer outputMu.Unlock()
	os.Stdout.Write(encoded)
	os.Stdout.Write([]byte{'\n'})
}
