# Saucer Nights protocol

Everything travels as ordinary chat lines on one channel (a cross-world linkshell by default). The game server already
authenticates who sent each line, so a player's chat identity (`Name@World`) is their identity at the table.

## Lines

```
SN1~<ChatBase64(frame)>
frame = u16 tableId | varint seq | u8 type | payload | u16 crc16
```

- **ChatBase64** is base64 with a chat-safe alphabet: no vowels (the profanity filter can't form words), no `<`/`>`
  (placeholders such as `<t>`), and no `/`, `*`, `%` or quotes. Alphabet:
  `BCDFGHJKLMNPQRSTVWXZbcdfghjklmnpqrstvwxz0123456789-_.,!=+()[]:;?`
- **CRC-16/CCITT** catches mangled lines (e.g. masked by a filter); they're dropped and repaired by the next snapshot.
- Lines stay ≤ 440 characters, leaving room for the channel command inside the game's 500-byte limit. Longer frames
  are split into `Fragment` frames (`fragId, index, count, chunk`) and reassembled per sender.
- One client sends at most one line per 1.2 s (configurable). Host frames are built lazily at send time, so a slow
  channel always carries the newest snapshot and the events since the last one.

## Messages

| Type | Dir | Payload |
|---|---|---|
| `Discover` (1) | player → all | — (hosts answer with `Announce`) |
| `Join` (2) | player → host | nonce, protocol version, P-256 public key (64 B), preferred seat |
| `Leave` (3) | player → host | — |
| `Action` (4) | player → host | round, turn id, kind, amount, hand index |
| `Bet` (5) | player → host | round, amount, shuffle seed (8 B) |
| `Ready` (6) | player → host | hand, shuffle seed (poker, provably fair tables) |
| `Resync` (7) | player → host | needs: roster / state / welcome |
| `Rebuy` (8), `SitOut` (9) | player → host | —, flag |
| `Announce` (32) | host → all | game, version, title, host name, seats, flags, rules |
| `Welcome` (33) / `Reject` (34) | host → player | nonce + seat + host public key / nonce + reason |
| `Roster` (35) | host → all | version, (seat, Name@World, flags)… |
| `State` (36) | host → all | game, roster version, flags, recent events, full table snapshot |
| `Closed` (38) | host → all | reason, text |

Moves carry the snapshot's `turnId`, so retries are idempotent and stale moves are ignored. `Join` from a player who is
already seated re-keys them and keeps their seat and chips.

## Snapshots

Every `State` is the complete public table: phase, seats, stacks, bets, visible cards, pots, whose turn it is, the
moves they may make, and a seconds-remaining deadline. Clients replace their view with it and animate the difference,
so a missed line never desynchronises anyone. The host repeats the snapshot after 9 s of quiet during play (30 s when
idle) and announces the table every 50 s; a client that hears nothing for 150 s treats the table as lost.

Hold'em hole cards travel inside the snapshot, sealed per seat:

```
key    = SHA-256-KDF(ECDH(host, player), "SaucerNights/ecdh/v1")
stream = HMAC-SHA256(key, "SaucerNights/hole/v1" | table | hand | seat)
sealed = (cards XOR stream) | first 2 bytes of HMAC(key, "SaucerNights/tag/v1" | table | hand | seat | cards)
```

## Provably fair shuffles

1. When a round/hand begins the host picks a 16-byte server seed and publishes `commit = SHA-256("…commit/v1" | seed)[0..16]`.
2. Players then send 8-byte seeds (with their blackjack bet, or a `Ready` in poker).
3. `deckKey = HMAC-SHA256(serverSeed, "…deck/v1" | table | round | (seat | seed)… in seat order)`; the deck is a
   Fisher–Yates shuffle driven by `HMAC-SHA256(deckKey, counter)` with rejection sampling.
4. At settlement the host reveals the server seed and the seeds used. Each client checks the commitment, that its own
   seed was included, and that the cards it saw are the ones the deck produces in dealing order (blackjack: initial
   deal, dealer cards, and the multiset of all cards).

### Hold'em: fair without exposing folded hands

Revealing the seed lets anyone rebuild the whole deck, which in poker would expose every folded hand. Hold'em therefore
adds a second, never-revealed secret (`PokerFair`):

1. With the server seed the host picks a 16-byte mask seed. From it it derives a permutation of the 52 deck positions
   and assigns them to *entries*: entry 0 = flop (3 positions), 1 = turn, 2 = river, 3+seat = that seat's two hole
   cards. Each entry gets an 8-byte salt and a hash
   `leaf = SHA-256("…poker-leaf/v2" | table | hand | entry | salt | positions)[0..10]`.
2. The published commitment is `SHA-256("…poker-commit/v2" | serverSeed | leaf…)[0..16]`. It is sent before any player
   seed arrives, so the host fixes which positions everyone gets before the deck exists.
3. The deck is shuffled from the server seed and the players' seeds as above; a card is `deck[position]`.
4. At the end of the hand the host reveals the server seed, the players' seeds and one item per entry: salt and
   positions for the board cards that were dealt and for every hand that was shown, the bare hash for everything else.
   Clients rebuild the commitment, check that no position is used twice, and check the board, the shown hands and (if
   opened) their own cards against the deck.

Unshown hands, undealt board cards and empty seats stay hashes, so their cards cannot be derived. A player's own cards
are only checked against the commitment when they are shown.

## Showing cards after the hand

While the table is in `Showdown` (also after everyone folded to one player), any dealt-in player whose cards are still
face down may send `Action { kind = Show (6), round = hand }`. The host turns the cards face up in the next snapshot,
opens that seat's fairness entry, emits a `ShowCards` event and keeps the hand on the table for at least 4 more seconds.
