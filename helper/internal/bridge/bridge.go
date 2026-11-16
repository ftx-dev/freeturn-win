// Package bridge связывает приложение с ядром free-turn-proxy:
// команды приходят строками JSON, события уходят строками JSON.
package bridge

import (
	"bufio"
	"encoding/json"
	"io"
	"sync"
	"time"

	"github.com/samosvalishe/free-turn-proxy/mobile"
)

type command struct {
	Cmd     string `json:"cmd"`
	Config  string `json:"config"`
	IfIndex int    `json:"ifIndex"`
	DNS     string `json:"dns"`
}

type event struct {
	Ev      string `json:"ev"`
	Version string `json:"version,omitempty"`
	State   string `json:"state,omitempty"`
	Streams int    `json:"streams"`
	Total   int    `json:"total"`
	Err     string `json:"err,omitempty"`
	Level   string `json:"level,omitempty"`
	Msg     string `json:"msg,omitempty"`
	Ts      int64  `json:"ts,omitempty"`
	URL     string `json:"url"`
	Manual  bool   `json:"manual"`
	Tx      int64  `json:"tx"`
	Rx      int64  `json:"rx"`
	TxRate  int64  `json:"txRate"`
	RxRate  int64  `json:"rxRate"`
	Args    string `json:"args,omitempty"`
	Cmd     string `json:"cmd,omitempty"`
}

// emitter сериализует вывод: события приходят из горутин ядра.
type emitter struct {
	mu  sync.Mutex
	enc *json.Encoder
	// Последнее отправленное состояние - чтобы опрос не дублировал push ядра.
	lastState string
}

func (e *emitter) send(ev event) {
	e.mu.Lock()
	defer e.mu.Unlock()
	if ev.Ev == "state" {
		e.lastState = ev.State
	}
	_ = e.enc.Encode(ev) // приложение умерло - об этом скажет EOF на stdin
}

func (e *emitter) stateChanged(s string) bool {
	e.mu.Lock()
	defer e.mu.Unlock()
	return e.lastState != s
}

type sink struct{ e *emitter }

func (s sink) OnState(state string, streams, total int, errMsg string) {
	s.e.send(event{Ev: "state", State: state, Streams: streams, Total: total, Err: errMsg})
}

func (s sink) OnLog(level, msg string, unixMillis int64) {
	s.e.send(event{Ev: "log", Level: level, Msg: msg, Ts: unixMillis})
}

func (s sink) OnCaptcha(url string, manual bool) {
	s.e.send(event{Ev: "captcha", URL: url, Manual: manual})
}

type protector struct{}

func (protector) Protect(fd int) bool { return bindToInterface(fd, int(boundIf.Load())) }

// Run обслуживает команды до EOF на in. EOF значит, что приложение умерло:
// ядро останавливается, чтобы не осталось сессии без хозяина.
func Run(in io.Reader, out io.Writer, stateDir string) error {
	e := &emitter{enc: json.NewEncoder(out), lastState: mobile.StateIdle}

	mobile.SetStateDir(stateDir)
	mobile.SetLogBuffer(false)
	mobile.SetEventSink(sink{e})
	mobile.SetProtect(protector{})
	defer func() {
		mobile.Stop()
		mobile.SetEventSink(nil)
		mobile.SetProtect(nil)
	}()

	e.send(event{Ev: "hello", Version: mobile.Version()})

	stopPoll := make(chan struct{})
	defer close(stopPoll)
	go poll(e, stopPoll)

	sc := bufio.NewScanner(in)
	sc.Buffer(make([]byte, 1<<20), 1<<20)
	for sc.Scan() {
		var c command
		if err := json.Unmarshal(sc.Bytes(), &c); err != nil {
			e.send(event{Ev: "error", Cmd: "parse", Err: err.Error()})
			continue
		}
		handle(e, c)
	}
	return sc.Err()
}

func handle(e *emitter, c command) {
	switch c.Cmd {
	case "start":
		SetInterface(c.IfIndex)
		if err := mobile.Start(c.Config); err != nil {
			e.send(event{Ev: "error", Cmd: "start", Err: err.Error()})
		}
	case "stop":
		mobile.Stop()
		e.send(event{Ev: "state", State: mobile.StateIdle})
	case "reconnect":
		SetInterface(c.IfIndex)
		if c.DNS != "" {
			mobile.SetDNSServers(c.DNS)
		}
		mobile.Reconnect()
	case "wake":
		mobile.Wake()
	case "args":
		args, err := mobile.ConfigToArgs(c.Config)
		if err != nil {
			e.send(event{Ev: "error", Cmd: "args", Err: err.Error()})
			return
		}
		e.send(event{Ev: "args", Args: args})
	default:
		e.send(event{Ev: "error", Cmd: c.Cmd, Err: "unknown command"})
	}
}

// poll раз в секунду отдаёт счётчики и ловит смену состояния, о которой ядро
// не сообщило push-событием (сессия завершилась сама).
func poll(e *emitter, stop <-chan struct{}) {
	t := time.NewTicker(time.Second)
	defer t.Stop()
	for {
		select {
		case <-stop:
			return
		case <-t.C:
			s := mobile.GetState()
			if e.stateChanged(s.State) {
				e.send(event{Ev: "state", State: s.State, Streams: s.Streams, Total: s.Total, Err: s.ErrMsg})
			}
			if s.State != mobile.StateIdle && s.State != mobile.StateError {
				e.send(event{Ev: "stats", Tx: s.TxTotal, Rx: s.RxTotal, TxRate: s.TxRate, RxRate: s.RxRate})
			}
		}
	}
}
