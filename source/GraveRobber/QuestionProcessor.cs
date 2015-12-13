﻿/*
 * GraveRobber. A .NET PoC program for fetching data from the SOCVR graveyards.
 * Copyright © 2015, ArcticEcho.
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.gnu.org/licenses/>.
 */





using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static GraveRobber.QuestionChecker;

namespace GraveRobber
{
    public class QuestionProcessor : IDisposable
    {
        private ConcurrentQueue<string> queuedUrls;
        private Logger<QueuedQuestion> watchedPosts;
        private bool dispose;

        public int WatchedPosts => watchedPosts?.Count ?? 0;

        public Logger<QuestionStatus> PostsPendingReview { get; }



        public QuestionProcessor()
        {
            queuedUrls = new ConcurrentQueue<string>();

            // Queued posts to check back on later.
            watchedPosts = new Logger<QueuedQuestion>("watched-posts.txt", new Func<QueuedQuestion, bool>(qq => false));
            watchedPosts.CollectionCheckedEvent = new Action(CheckPosts);

            // Save any active posts (rather than caching them).
            PostsPendingReview = new Logger<QuestionStatus>("posts-pending-review.txt", new Func<QuestionStatus, bool>(qq => false));

            Task.Run(() => ProcessUrlQueue());
        }

        ~QuestionProcessor()
        {
            Dispose();
        }



        public void WatchPost(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("'url' must not be null, empty, or entirely whitespace.", "url");
            }

            queuedUrls.Enqueue(url);
        }

        public void Dispose()
        {
            if (dispose) return;
            dispose = true;

            watchedPosts.Dispose();
            PostsPendingReview.Dispose();

            GC.SuppressFinalize(this);
        }




        private void ProcessUrlQueue()
        {
            var url = "";

            while (!dispose)
            {
                Thread.Sleep(2000);

                // Check for dupes (and other conditions).
                if (dispose || queuedUrls.Count == 0 ||
                    watchedPosts.Any(x => x.Url == url) ||
                    PostsPendingReview.Any(x => x.Url == url))
                {
                    continue;
                }

                queuedUrls.TryDequeue(out url);
                var date = GetQuestionStatus(url)?.CloseDate;

                // Ignore the post as it is either open or deleted.
                if (date == null) continue;

                watchedPosts.EnqueueItem(new QueuedQuestion
                {
                    Url = url,
                    CloseDate = (DateTime)date
                });
            }
        }

        private void CheckPosts()
        {
            var postsToRemove = new HashSet<QueuedQuestion>();

            foreach (var post in watchedPosts)
            {
                if ((DateTime.UtcNow - post.CloseDate).TotalDays < 1) continue;

                Thread.Sleep(2000);

                if (dispose) return;

                var status = GetQuestionStatus(post.Url);

                if (status?.CloseDate != null &&
                    status.EditsSinceClosure > 0)
                {
                    PostsPendingReview.EnqueueItem(status);
                    postsToRemove.Add(post);
                }
                else if (status?.CloseDate == null)
                {
                    // Remove the post as it has been either reopened or deleted.
                    postsToRemove.Add(post);
                }
            }

            foreach (var url in postsToRemove)
            {
                watchedPosts.RemoveItem(url);
            }
        }
    }
}
