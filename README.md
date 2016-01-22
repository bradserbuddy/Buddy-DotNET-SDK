# Buddy .NET SDK

## Overview

The Buddy .NET SDK helps you get up and running in seconds.  

For the most part, the Buddy .NET SDK takes care of all the housekeeping of making requests against the Buddy REST API:

* Building and formatting requests
* Managing authentication
* Parsing responses
* Loading and saving credentials

With that handled, all you have to do is initialize the SDK and start making some calls!

## Getting Started

To get started with the Buddy Platform SDK, please reference the _Getting Started_ series of documents at [buddyplatform.com/docs](https://buddyplatform.com/docs). You will need an application ID and key before you can use the SDK. The _Getting Started_ documents will walk you through obtaining everything you need and show you where to find the SDK for your platform.

Application IDs and keys are obtained at the Buddy Developer Dashboard at [buddyplatform.com](https://buddyplatform.com/login).

Full documentation for Buddy's services are available at [buddyplatform.com/docs](https://buddyplatform.com/docs).

### Prerequisites

* Visual Studio 2013 or greater
* Xamarin Studio 5.9.7 or greater (iOS and Android)

The Buddy .NET SDK can be accessed via [NuGet](http://nuget.org/). NuGet version of at least 2.8.6 is required to install the Buddy SDK and sample apps.

### Getting Started

The Buddy .NET SDK is distributed via NuGet. Source code for the SDK can be cloned from [GitHub](https://github.com/buddyplatform).

#### Install with NuGet

We recommend using NuGet to include the SDK in your project. It's fast and makes it much easier to keep up to date with the latest SDK release.

To include the Buddy .NET SDK in your project:
* Right-click on your project in Visual Studio and select the "Manage NuGet Packages..." menu item.
* Type "BuddyPlatformSdk" into the edit box in the upper-right-hand corner of the "Manage NuGet Packages" window.
* Click "Install".

#### Install from Source

Buddy hosts our SDK source on GitHub. To access it, you need to have a GitHub account, and you will also need [Git](http://git-scm.com/download) installed. If you'd like to contribute SDK modifications or additions to Buddy, you'll want to [fork the repository](https://help.github.com/articles/fork-a-repo) so you can issue [pull requests](https://help.github.com/articles/be-social#pull-requests). See the "Contributing Back" section below for details.

1) In a Terminal window run:

    git clone https://github.com/BuddyPlatform/Buddy-DotNET-SDK.git

This will clone the latest version of the SDK into a directory called **Buddy-DotNET-SDK**.

2) Navigate to the **Buddy-DotNET-SDK** directory that was created when you cloned the Buddy GitHub repository.

The .NET source is in the **Buddy-DotNET-SDK\Src** directory.

##### Build the GitHub Source

You must add a reference to the Buddy SDK in order to use it in your application.
* Open your project in Visual Studio
* In the Solution Explorer, right-click on your project and select "Add Reference..." (about halfway down the menu)
* In the Reference Manager, click the "Browse..." button in the lower-right-hand corner of the window
* In the "Select the files to reference..." dialog, find the assembly file for your project type and click "Add"
* Click "OK" in the "Reference Manager" window

Now when you build your project, Visual Studio will build the SDK.

## Using the .NET SDK

Visit the [Buddy Dashboard](https://buddyplatform.com) to obtain your application ID and key.

### Initialize the SDK

To reference the Buddy SDK in your source file, you need to put a 'using' keyword at the top of files that will contain code that calls Buddy:

    using BuddySDK;

The `Init` method should be called once at the start of your app; we recommend placing it in your project's application constructor.

    public MyCoolApp()
    {
        // Don't forget to get your app ID and key from http://buddyplatform.com!
        Buddy.Init("YOUR_APP_ID", "YOUR_APP_KEY");
     }
     
Replace "YOUR_APP_ID" and "YOUR_APP_KEY" above with your Buddy app's ID and key from the [Buddy Dashboard](https://buddyplatform.com).

### User Flow

The Buddy .NET SDK handles user creation, login, and logout. Here are some example calls.

#### Create A User
    
    // We recommend awaiting Buddy calls; see https://msdn.microsoft.com/en-us/library/hh191443.aspx for more details.
    await Buddy.CreateUserAsync(username, password);

#### User Login

    var result = await Buddy.LoginUserAsync(username, password);
    // Obtain the user's userName with result.Value.userName;

#### User Logout

    await Buddy.LogoutUserAsync();

#### User Authorization event handler

You can add an event handler to `AuthorizationNeedsUserLogin` that gets called whenever a Buddy call is made that requires a logged-in user. That way, you won't have to manage user login state. Here's an example:

    Buddy.AuthorizationNeedsUserLogin += async (sender, args) =>
        {
            await Buddy.LoginUserAsync("testuser", "testpassword");
        };
        
### REST Interface

Each SDK provides general wrappers that make REST calls to Buddy.

#### GET

    // GET all checkins within a 5000 meter radius around the point 47.1, -122.3
    var result = await Buddy.GetAsync<PagedResult<Checkin>>("/checkins", new { locationRange = "47.1,-122.3,5000" } );

#### POST

    var result = await Buddy.PostAsync<Checkin>("/checkins", new {
                            location = new BuddyGeoLocation(47.1, -122.3),
                            comment =  "This place was awesome!"
                        });
    // POST results return similar responses to GET, use the result to check for success

#### PUT/PATCH/DELETE

Each remaining REST verb is available through the Buddy SDK using the same pattern as the POST and GET examples.

### Working With Files

Buddy offers support for binary files. The .NET SDK works with files through our REST interface similarly to other API calls.

**Note:** Responses for files deviate from the standard Buddy response templates. See the [Buddy Platform documentation](https://buddyplatform.com/docs) for more information.

#### Upload A File

Here we demonstrate uploading a picture. For all binary files (e.g. blobs and videos), the pattern is the same, but with a different path and different parameters.

    // Create a new BuddyFile with the picture we want to upload
    var result = await Buddy.PostAsync<Picture> ("/pictures", new {
                            data = new BuddyFile (chosenImage.AsPNG().AsStream(), "data", "image/png"),
                        });

#### Download A File

Our download example uses pictures.

    // Gets the photo bits, resized to 200x200
    var result = await Buddy.GetAsync<BuddyFile>("/pictures/" + id + "/file", new {size=200});

    if (result.IsSuccess && result.Value != null) {
        
        // Load the image data from result.Value.Data
    }

### Advanced Usage

#### Automatically report location for each Buddy call

If you set the current location in the Buddy client, each time a Buddy call is made that location will be passed in the call. Most calls that send data to Buddy have a location parameter; if a call is made that doesn't take location, the parameter will be ignored.

    Buddy.LastLocation = new BuddyGeoLocation(42, -42);

#### Multiple concurrent users

If you need to have multiple clients (for example if you need to interact with multiple users concurrently from your app), you can capture clients created from `Buddy.init` and use those clients individually:

    var client1 = Buddy.Init("App ID 1", "App Password 1");

    var client2 = Buddy.Init("App ID 2", "App Password 2");

    await client1.LoginUserAsync("username1", "password1");

    await client2.LoginUserAsync("username2", "password2");
    
#### Handling connectivity

You can add an event handler to `ConnectivityLevelChanged` if you would like to be notified if your device loses and regains ability to communicate to the Buddy servers for whatever reason. Here's an example that notifies the user:

    Buddy.ConnectivityLevelChanged += (object sender, ConnectivityLevelChangedArgs e) =>
    {
        MessageBox.Show(e.ConnectivityLevel == ConnectivityLevel.None ? "No connectivty..." : "Connected!";
    };

### Sample Apps

#### PushSample (Windows Phone 8)

A simple app that demonstrates user login\logout and the push notification API.

#### BuddySquare (Xamarin iOS)

An app that demonstrates user login\logout\creation and the checkin, pictures, and metrics APIs.

## Contributing Back: Pull Requests

We'd love to have your help making the Buddy SDK as good as it can be!

To submit a change to the Buddy SDK please do the following:

1. Create your own fork of the Buddy SDK
2. Make the change to your fork
3. Before creating your pull request, please sync your repository to the current state of the parent repository: `git pull origin master`
4. Commit your changes, then [submit a pull request](https://help.github.com/articles/using-pull-requests) for just that commit

## Questions or need help?

This should have given you the basics of how to work with the Buddy .NET SDK. If you have further questions or are stuck, send an email to support@buddy.com.