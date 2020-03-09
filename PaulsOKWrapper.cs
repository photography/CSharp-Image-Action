using System;
using System.Text;
using System.IO;
using Octokit;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace CSharp_Image_Action
{
    // It's just an OK wrapper, not a great wrapper
    // we are wrappign OctoKit
    public class PaulsOKWrapper
    {
        protected string repo = "Repo";
        protected string owner = "Owner";

        bool gitHubStuff = false;
        private bool cleanlyLoggedIn = false;
        private GitHubClient github = null;
        System.IO.DirectoryInfo repoDirectory; 

        public GitHubClient GithubClient { get => github; set => github = value; }
        public bool CleanlyLoggedIn { get => cleanlyLoggedIn; }
        public DirectoryInfo RepoDirectory { get => repoDirectory; set => repoDirectory = value; }
        public bool DoGitHubStuff { get => gitHubStuff; set => gitHubStuff = value; }

        protected string email;
        protected string username;

        public void TestCleanlyLoggedIn()
        {
            if(!cleanlyLoggedIn)
            {
                Console.WriteLine("We have not cleanlyLogged In, but we are trying to do stuff!");
                Console.WriteLine(System.Environment.StackTrace);
            }
        }

        public PaulsOKWrapper(bool gitHubStuff)
        {
            this.gitHubStuff = gitHubStuff;
            this.username = "GitHub Action";
            this.email = "actions@users.noreply.github.com";
        }

        public bool SetOwnerAndRepo(string p_Owner, string p_Repo)
        {
            this.owner = p_Owner;
            this.repo = p_Repo;
            Console.WriteLine("Owner Set to : " + this.owner);
            Console.WriteLine("Repo set to : " + this.repo);
            return true;
        }

        public bool AttemptLogin()
        {
            try{
                Console.WriteLine("Loading github...");
                string secretkey = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                github = new GitHubClient(new ProductHeaderValue("Pauliver-ImageTool"))
                {
                    Credentials = new Credentials(secretkey)
                };
                cleanlyLoggedIn = true; // or maybe
                Console.WriteLine("... Loaded");
                return true;
            }catch(Exception ex){
                Console.WriteLine(ex.ToString());
                Console.WriteLine("... Loading Failed");
                cleanlyLoggedIn = false;
                return false;
            }
        }

        protected string CurrentBranchName;
        protected string TargetBranchName;
        protected string AutoMergeLabel;
        bool SetupForMergByLabel = false;
        public bool SetupForMergesByLabel(string p_autoMergeLabel = "automerge", string currentBranch = "master", string targetBranch = "gh-pages")
        {
            this.CurrentBranchName = currentBranch;
            this.TargetBranchName = targetBranch;
            this.AutoMergeLabel = p_autoMergeLabel;

            SetupForMergByLabel = true;

            return true;
        } 

        protected string headMasterRef;
        protected Reference masterReference;
        protected Commit latestCommit;
        protected NewTree UpdatedTree;

        async public ValueTask<bool> SetupCommit()
        {
            TestCleanlyLoggedIn();
            //https://laedit.net/2016/11/12/GitHub-commit-with-Octokit-net.html
            try
            {
                headMasterRef = "heads/master";
                // Get reference of master branch
                masterReference = await github.Git.Reference.Get(owner, repo, headMasterRef);
                // Get the laster commit of this branch
                latestCommit = await github.Git.Commit.Get(owner, repo, masterReference.Object.Sha);

                UpdatedTree = new NewTree {BaseTree = latestCommit.Tree.Sha };

            }catch(Exception ex)
            {
                cleanlyLoggedIn = false;

                Console.WriteLine(ex.ToString());
                
                return false;
            }
            return true;
        }

        const string NEWFILE = "NEW-FILE";
        const string TOOBIG = "TOO-BIG";

        const string MULTIPLERESULTS = "MULTIPLE-RESULTS";
        const string ZERORESULTS = "ZERO-RESULTS";
        private const string COMMITMESSAGE = "Time's up, let's do this";

        // Flip this to a bitmask enum return? and a string with an SHA, so we can pack the bitmask with the flags we need, and return the SHA
        async public Task<string> GetTextFileSHA(string filename)
        {
            // https://developer.github.com/v3/repos/contents/#get-contents
            // if this is the first time we have seen the file return 
            var content = await github.Repository.Content.GetAllContents(owner,repo,filename);
            if(content.Count > 1)
            {
                return MULTIPLERESULTS;
            }else if(content.Count == 0)
            {
                return ZERORESULTS;
            }
            // should we consider testing for (content[0].Size) so we can use "too big" ?
            return content[0].Sha;
        }

        async public ValueTask<bool> ImmediatlyAddorUpdateTextFile(System.IO.FileInfo fi)
        {
            // We are using an API that has a limit of 1mb files
            // so this will not work for our images
            TestCleanlyLoggedIn();
            string filename = fi.FullName.Replace(repoDirectory.FullName,"");
            string SHA = await GetTextFileSHA(filename);
            
            try
            {
                string filecontnet = File.ReadAllText(fi.FullName);

                // This is one implementation of the abstract class SHA1.
                var File_SHA = SHA1Util.SHA1HashStringForUTF8String(filecontnet);

                if(File_SHA == SHA)
                {
                    Console.WriteLine("File SHA's are the same, no changes. Not creating or committing, exiting");
                    return true;
                }

                if(SHA == ZERORESULTS)
                {
                    Console.WriteLine("retrieved Zero results, was expecting one. Creating file instead");
                    var temp = await github.Repository.Content.CreateFile(owner,repo,filename,new CreateFileRequest("Created " + fi.Name,filecontnet));
                }
                else if(SHA == MULTIPLERESULTS)
                {
                    Console.WriteLine("retrieved multiple results, was expecting one. can't continue");
                    return false;
                }
                else if(SHA == TOOBIG)
                {
                    Console.WriteLine("attempted to retrieve a file over 1mb from an API that limits to 1mb");
                    return false;
                }else{
                    var temp = await github.Repository.Content.UpdateFile(owner,repo,filename,new UpdateFileRequest("Updated " + fi.Name,filecontnet, SHA));
                }

            }catch(Exception ex)
            {
                cleanlyLoggedIn = false;

                Console.WriteLine(ex.ToString());
                
                return false;
            }
            return true;
        }

        public async ValueTask<bool> FindStalePullRequests(string PRname)
        {
            TestCleanlyLoggedIn();

            bool ShouldClose = false;

            var prs = await github.PullRequest.GetAllForRepository(owner,repo);
            
            foreach(PullRequest pr in prs)
            {
                foreach(Label l in pr.Labels)
                {
                    ShouldClose = false;
                    if(l.Name == AutoMergeLabel && pr.Title.Contains(PRname)) // I'm left over from a previous run
                    {
                        ShouldClose = true;
                    }
                    if(ShouldClose)
                    {
                        Console.WriteLine("It looks like you have an existing PR still open");
                        Console.WriteLine("This is likely to fail, unless you close : " + pr.Title);
                    }
                    ShouldClose = false;
                }
            }
            return true;
        }

        async public ValueTask<bool> CreateAndLabelPullRequest(string PRname)
        {
            TestCleanlyLoggedIn();

            Console.WriteLine("PR: " + PRname);
            Console.WriteLine("Owner: " + owner);
            Console.WriteLine("CurrentBranch: " + CurrentBranchName);
            Console.WriteLine("TargetBranch: " + TargetBranchName);

            NewPullRequest newPr = new NewPullRequest(PRname + " : " + System.DateTime.UtcNow.ToString(),CurrentBranchName,TargetBranchName);
            PullRequest pullRequest = await github.PullRequest.Create(owner,repo,newPr);
            
            Console.WriteLine("PR Created # : " + pullRequest.Number);

            Console.WriteLine("PR Created: " + pullRequest.Title);

            //var prupdate = new PullRequestUpdate();
            //var newUpdate = await github.PullRequest.Update(Owner,Repo,pullRequest.Number,prupdate);

            try{

                Console.WriteLine("Owner: " + PRname);        
                Console.WriteLine("Repo: " + repo);
                Console.WriteLine("pullRequest.Number: " + pullRequest.Number);

                if(github == null){  
                    Console.WriteLine("github == null");
                }
                if(github.Issue == null){
                    Console.WriteLine("github.Issue == null");
                }

                var issue = await github.Issue.Get(owner, repo, pullRequest.Number);
                if(issue != null) //https://octokitnet.readthedocs.io/en/latest/issues/
                {
                    var issueUpdate = issue.ToUpdate();
                    if(issueUpdate != null)
                    {
                        issueUpdate.AddLabel(AutoMergeLabel);
                        var labeladded = await github.Issue.Update(owner, repo, pullRequest.Number, issueUpdate);
                        Console.WriteLine("Label Added: " + AutoMergeLabel);
                    }
                }

            }catch(Exception ex){
                cleanlyLoggedIn = false;
                Console.WriteLine(ex.ToString());  
                return false; 
            }
        return true;
        }


        async public ValueTask<bool> AddorUpdateFile(System.IO.FileInfo fi)
        {
            TestCleanlyLoggedIn();
            try
            {
                // For image, get image content and convert it to base64
                var imgBase64 = Convert.ToBase64String(File.ReadAllBytes(fi.FullName));
                
                // Create image blob
                var imgBlob = new NewBlob { Encoding = EncodingType.Base64, Content = (imgBase64) };
                var imgBlobRef = await github.Git.Blob.Create(owner, repo, imgBlob);

                UpdatedTree.Tree.Add(new NewTreeItem { Path = fi.FullName.Replace(repoDirectory.FullName,""), Mode = "100644", Type = TreeType.Blob, Sha = imgBlobRef.Sha });

                // Is the file in the repo?
                // - if not add it
                // - if it is update it
            }catch(Exception ex)
            {
                cleanlyLoggedIn = false;

                Console.WriteLine(ex.ToString());
                
                return false;
            }
            return true;
        }

        async public ValueTask<bool> SomethingAboutCommittingAnImage(System.IO.FileInfo fi)
        {
            return await AddorUpdateFile(fi);
        }

        async public ValueTask<bool> CommitAndPush()
        {
            TestCleanlyLoggedIn();
            try{
                var newTree = await github.Git.Tree.Create(owner, repo, UpdatedTree);
                var newCommit = new NewCommit("Updated Images and json files", newTree.Sha, masterReference.Object.Sha);
                var commit = await github.Git.Commit.Create(owner, repo, newCommit);
                var headMasterRef = "heads/master";
                // Update HEAD with the commit
                await github.Git.Reference.Update(owner, repo, headMasterRef, new ReferenceUpdate(commit.Sha));
            }catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return false;
            }
            return true;
        }

        async public ValueTask<bool> MergePullRequest(string CommitMessage = COMMITMESSAGE)
        {
            Console.WriteLine("Merging pull requests For: " + owner + " \\ " + repo );
            Console.WriteLine(" - With Label : " + AutoMergeLabel );
            Console.WriteLine(" - Message will be : " + CommitMessage);
            if(CommitMessage == COMMITMESSAGE)
            {
                Console.WriteLine(" - LLLLEEEERROOOYYYY JENKINS");
            }

            bool shouldmerge = false;

            var prs = await github.PullRequest.GetAllForRepository(owner,repo);
                
            foreach(PullRequest pr in prs)
            {
                shouldmerge = false; // Reset state in a loop
                Console.WriteLine("Found PR: " + pr.Title);

                foreach(Label l in pr.Labels)
                {

                    if(l.Name == AutoMergeLabel)
                    {
                        shouldmerge = true;
                    }
                    
                    if(false)// Add your own conditions here, or perhaps a "NEVER MERGE" label?
                    {
                        shouldmerge = true;
                    }

                }
                if(shouldmerge)
                {
                    MergePullRequest mpr = new MergePullRequest();
                    mpr.CommitMessage = CommitMessage;
                    mpr.MergeMethod = PullRequestMergeMethod.Merge;
                    
                    var merge = await github.PullRequest.Merge(owner,repo,pr.Number,mpr);
                    if(merge.Merged)
                    {
                        Console.WriteLine("-> " + pr.Number + " - Successfully Merged");
                    }else{
                        Console.WriteLine("-> " + pr.Number + " - Merge Failed");
                    }
                }
                shouldmerge = false; // Reset state in a loop
            }
            return true;
        }
        
    }

    public class PaulsOKSIngleton
    {

    }
}