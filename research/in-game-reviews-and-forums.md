# In-game reviews and forum posts

Research date: 2026-09-09. Local source commit: `7d0e60fedb84bba6c9bfc89681d38fb7f77f27ee`.

## Result

The review system is not a general message API. It has a package-specific route and a protected submission method. No supported forum submission API was found in the checked game source or official API documentation. This does not prove that the website has no private endpoint.

## Review request path

1. Game code can call `Game.Overlay.ShowReviewModal(package)`. This enters the menu scope and opens the built-in form. [Source](../engine/Sandbox.Engine/Game/Game/Game.Overlay.cs#L100), [official API](https://sbox.game/api/Sandbox.Game.Overlay/ShowReviewModal).
2. The form loads the local player's existing review. The user selects a score, enters text, and can select positive and negative tags. Submission requires a score and non-empty text. [Source](../game/addons/menu/Code/Modals/ReviewModal.razor#L90).
3. The form calls `MenuUtility.PostReview`, which calls the internal `Review.Post` method. Normal game code does not have public access to that method. [Menu source](../engine/Sandbox.Menu/MenuUtility.cs#L324), [review source](../engine/Sandbox.Engine/Game/Services/Reviews.cs#L159).
4. The service uses `POST /package/reviews/{packageIdent}` with `text`, `rating`, `positives`, and `negatives`. The runtime backend base URL is `https://public.facepunch.com/sbox`. The runtime request handler adds `Authorization: session ...` and `X-User-Id` when an account session exists. These facts describe the client implementation; server authentication rules were not tested. The separate `ServiceApi.SetApiKey` Bearer path must not be confused with this runtime session path. [Route](../engine/Sandbox.Services/Api/IPackageApi.cs#L48), [backend setup](../engine/Sandbox.Services/Implementation.cs#L28), [runtime headers](../engine/Sandbox.Engine/Services/Api/Api.cs#L196).

The public Review API lists `Fetch`, `FetchEx`, and `Get`, but no submission method. [Official API](https://sbox.game/api/Sandbox.Services.Review).

`Review.Post` catches exceptions without reporting them. Thus, closure of the current review form alone is not proof that the service saved a review. [Source](../engine/Sandbox.Engine/Game/Services/Reviews.cs#L159).

## Forum API boundary

The website has game-specific forums under `/f/{packageIdent}/`. Its [new-thread page](https://sbox.game/f/general/create-thread) requires website login. The [public Live Services index](https://sbox.game/api/i/liveservices) has no Forum service. These findings support a website posting route, but do not establish a public REST submission endpoint.

The service client has no forum interface. The forum protobuf file defines `ThreadPosted`, `ReplyPosted`, and `ThreadEdited` messages with identifiers. These are event-shaped messages, not methods that accept a title and post text. They do not establish a supported submission path. [Service interfaces](../engine/Sandbox.Services/Api/ServiceApi.cs#L6), [forum messages](../engine/Sandbox.Services/ProtoBuf/Forum/Forum.cs#L3).

Changing the review URL is therefore not a verified solution. Forum submission would need a confirmed route, input format, and authentication contract that permits the signed-in player to submit a post.

## Available designs

- For the existing sbox.game forum, a link to the game's forum page is the simplest option. The user completes the post on the website. Direct submission from the game needs a supported Facepunch interface.
- For a forum service that you control, an in-game form can send an HTTP request to your backend. `Sandbox.Services.Auth.GetToken(serviceName)` can provide a token that your backend validates to identify the Steam user. Your backend must separately implement forum permissions and storage. The documented token flow does not grant permission to post to the sbox.game forum. [HTTP documentation](https://sbox.game/dev/doc/networking/http-requests), [token documentation](https://sbox.game/dev/doc/services/auth-tokens).

## Evidence limits

This was source and documentation research. No review or forum post was sent. No live authenticated endpoint test was performed. A custom engine change would not, by itself, prove that the production forum backend accepts the request.
